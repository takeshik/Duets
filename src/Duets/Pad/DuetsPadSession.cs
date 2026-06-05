using System.Collections.Concurrent;
using System.Threading.Channels;
using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Duets.Pad.State;
using Duets.Pad.Timeline;

namespace Duets.Pad;

/// <summary>
/// Isolated server-side runtime for one DuetsPad browser session.
/// </summary>
/// <remarks>
/// <para>
/// A DuetsPad session owns one DuetsSession and keeps the associated Canvas,
/// Timeline, and object-renderer state together. The service layer is
/// responsible for creating session identifiers, routing HTTP/SSE requests to
/// the matching session, and disposing idle or explicitly reset sessions.
/// </para>
///
/// <para>
/// <b>Thread-safety and locking model</b><br/>
/// Public eval entry (<see cref="EvaluateAsync"/>) is serialized by <c>_evalSemaphore</c>
/// (SemaphoreSlim(1,1)). This is intentional: <see cref="DuetsSession"/> itself throws on
/// concurrent use, and eval drives all side effects (dump, canvas.*, console).
/// </para>
///
/// <para>
/// Internal ops — <see cref="Dump"/>, <see cref="CanvasAdd"/>, <see cref="CanvasSet"/>,
/// <see cref="CanvasClear"/> — are called synchronously <em>from within</em> the eval call
/// stack. They therefore MUST NOT re-acquire <c>_evalSemaphore</c> (deadlock). They share a
/// separate <c>_stateLock</c> object that is also held by subscriber registration. This is the
/// <em>only</em> lock used for state mutation + event dispatch, and it is never held across an
/// I/O boundary.
/// </para>
///
/// <para>
/// <b>Initial-event ordering</b><br/>
/// When a new SSE subscriber connects, the initial event must reflect a state that is at least
/// as new as any update the subscriber could subsequently receive. To guarantee this without a
/// TOCTOU gap, <see cref="AddCanvasSubscriber"/> and <see cref="AddTimelineSubscriber"/> acquire
/// <c>_stateLock</c> and, <em>while still holding it</em>, both register the writer and
/// immediately enqueue the current-state initial event (<c>canvas.snapshot</c> /
/// <c>timeline.reset</c>) to that writer. Subsequent mutations enqueue to all registered
/// writers under the same lock. As a result a subscriber either (a) registers before a mutation
/// and therefore sees the initial event followed by the update, or (b) registers after the
/// mutation and sees the post-mutation initial event — neither can observe the post-mutation
/// state first.
/// </para>
///
/// <para>
/// All enqueues use <see cref="ChannelWriter{T}.TryWrite"/> (non-blocking). A slow or
/// disconnected subscriber is silently dropped if its channel is full; it should be removed
/// and its channel completed via <see cref="RemoveCanvasSubscriber"/> /
/// <see cref="RemoveTimelineSubscriber"/>.
/// </para>
/// </remarks>
internal sealed class DuetsPadSession : IDisposable
{
    // Serializes public eval entry; NOT re-acquired by internal ops.
    private readonly SemaphoreSlim _evalSemaphore = new(1, 1);

    // Guards state mutation and subscriber enqueue. Never held across I/O.
    private readonly object _stateLock = new();

    private readonly ConcurrentDictionary<
        Guid,
        ChannelWriter<CanvasEventMessage>
    > _canvasSubscribers = new();

    private readonly ConcurrentDictionary<
        Guid,
        ChannelWriter<TimelineEventMessage>
    > _timelineSubscribers = new();

    private ObjectRenderingPipeline _pipeline;

    public DuetsPadSession(Guid id, DuetsSession duetsSession)
    {
        this.Id =
            id == Guid.Empty
                ? throw new ArgumentException("Session id cannot be empty.", nameof(id))
                : id;
        this.DuetsSession = duetsSession ?? throw new ArgumentNullException(nameof(duetsSession));

        this._pipeline = new ObjectRenderingPipeline(this.ObjectRenderers);

        // Subscribe to console output — runs synchronously on the eval thread.
        this.DuetsSession.ConsoleLogged += this.OnConsoleLogged;

        // Bind __padDump__ and override dump in JS without touching ScriptEngineInit.js.
        this.DuetsSession.SetValue("__padDump__", new Action<object?>(this.Dump));
        this.DuetsSession.Execute("dump = function (v) { __padDump__(v); return v; };");

        // Bind canvas and ui globals.
        this.DuetsSession.SetValue("canvas", new CanvasApi(this));
        this.DuetsSession.SetValue("ui", new UiApi(this._pipeline));

        // Register per-session d.ts declarations for canvas and ui.
        this.DuetsSession.Declarations.RegisterDeclaration(
            """
            // DuetsPad per-session globals
            declare const canvas: {
                /** Renders value and appends it as a new child of the canvas root. */
                add(value: any): void;
                /** Renders value and replaces all canvas children with it. */
                set(value: any): void;
                /** Clears all canvas children. */
                clear(): void;
            };

            declare const ui: {
                /** Returns a raw-HTML escape-hatch node (use sparingly). */
                rawHtml(content: string): any;
                /** Builds a structured element node. */
                element(tag: string, attributes?: any, children?: any[]): any;
                /** Returns a plain text node. */
                text(value: string): any;
                /** Returns a <span class="duetspad-label"> wrapping value. */
                label(value: string): any;
                /** Returns a <div class="duetspad-stack"> containing rendered children. */
                stack(children?: any[]): any;
                /** Builds a <table class="duetspad-table"> from rows. */
                table(rows: any[], options?: { columns?: string[] }): any;
            };
            """
        );
    }

    public Guid Id { get; }

    public DuetsSession DuetsSession { get; }

    public CanvasState Canvas { get; private set; } = CanvasState.Empty;

    public TimelineState Timeline { get; private set; } = TimelineState.Empty;

    public IReadOnlyList<IObjectRenderer> ObjectRenderers { get; private set; } = [];

    // -------------------------------------------------------------------------
    // State setters (kept for compatibility with any existing callers)
    // -------------------------------------------------------------------------

    public void SetCanvas(CanvasState canvas)
    {
        this.Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    public void SetTimeline(TimelineState timeline)
    {
        this.Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
    }

    public void SetObjectRenderers(IReadOnlyList<IObjectRenderer> objectRenderers)
    {
        this.ObjectRenderers =
            objectRenderers ?? throw new ArgumentNullException(nameof(objectRenderers));

        // Rebuild the pipeline so subsequent Dump/CanvasAdd/CanvasSet calls pick up the change.
        this._pipeline = new ObjectRenderingPipeline(this.ObjectRenderers);
    }

    // -------------------------------------------------------------------------
    // SSE subscriber registration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a Canvas SSE subscriber. A <c>canvas.snapshot</c> event for the current
    /// Canvas state is enqueued to <paramref name="writer"/> before this method returns,
    /// under the same lock used for all subsequent updates (see ordering guarantee in the
    /// class remarks).
    /// </summary>
    public Guid AddCanvasSubscriber(ChannelWriter<CanvasEventMessage> writer)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        var key = Guid.NewGuid();
        lock (this._stateLock)
        {
            this._canvasSubscribers[key] = writer;
            writer.TryWrite(CanvasEventMessage.Snapshot(this.Canvas));
        }

        return key;
    }

    /// <summary>Removes the Canvas subscriber identified by <paramref name="key"/>.</summary>
    public void RemoveCanvasSubscriber(Guid key) => this._canvasSubscribers.TryRemove(key, out _);

    /// <summary>
    /// Registers a Timeline SSE subscriber. A <c>timeline.reset</c> event for the current
    /// Timeline state is enqueued to <paramref name="writer"/> before this method returns,
    /// under the same lock used for all subsequent updates (see ordering guarantee in the
    /// class remarks).
    /// </summary>
    public Guid AddTimelineSubscriber(ChannelWriter<TimelineEventMessage> writer)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        var key = Guid.NewGuid();
        lock (this._stateLock)
        {
            this._timelineSubscribers[key] = writer;
            writer.TryWrite(TimelineEventMessage.Reset(this.Timeline, "initial"));
        }

        return key;
    }

    /// <summary>Removes the Timeline subscriber identified by <paramref name="key"/>.</summary>
    public void RemoveTimelineSubscriber(Guid key) =>
        this._timelineSubscribers.TryRemove(key, out _);

    // -------------------------------------------------------------------------
    // Internal ops — called from the eval call stack; must not acquire _evalSemaphore
    // -------------------------------------------------------------------------

    /// <summary>
    /// Renders <paramref name="value"/> and appends a Timeline entry with reason "dump".
    /// On render failure, appends an output-error marker entry instead. Never throws.
    /// </summary>
    internal void Dump(object? value)
    {
        try
        {
            ITerminalRenderNode body;
            try
            {
                body = this._pipeline.Render(value);
            }
            catch (Exception ex)
            {
                body = OutputError.Create($"Render error: {ex.Message}");
                this.AppendTimelineEntry("render-error", body);
                return;
            }

            this.AppendTimelineEntry("dump", body);
        }
        catch
        {
            // Absolute last resort: swallow so eval is never disrupted.
        }
    }

    /// <summary>
    /// Renders <paramref name="value"/> and appends it to the Canvas. On render failure,
    /// Canvas is unchanged and a Timeline output-error marker is appended. Never throws.
    /// </summary>
    internal void CanvasAdd(object? value)
    {
        try
        {
            ITerminalRenderNode node;
            try
            {
                node = this._pipeline.Render(value);
            }
            catch (Exception ex)
            {
                var errorBody = OutputError.Create($"Render error: {ex.Message}");
                this.AppendTimelineEntry("render-error", errorBody);
                return;
            }

            lock (this._stateLock)
            {
                this.Canvas = this.Canvas.Append(node);
                this.BroadcastCanvas();
            }
        }
        catch
        {
            // Swallow.
        }
    }

    /// <summary>
    /// Renders <paramref name="value"/> and replaces Canvas children with it. On render
    /// failure, Canvas is unchanged and a Timeline output-error marker is appended. Never throws.
    /// </summary>
    internal void CanvasSet(object? value)
    {
        try
        {
            ITerminalRenderNode node;
            try
            {
                node = this._pipeline.Render(value);
            }
            catch (Exception ex)
            {
                var errorBody = OutputError.Create($"Render error: {ex.Message}");
                this.AppendTimelineEntry("render-error", errorBody);
                return;
            }

            lock (this._stateLock)
            {
                this.Canvas = this.Canvas.Set(new ElementChildren(node));
                this.BroadcastCanvas();
            }
        }
        catch
        {
            // Swallow.
        }
    }

    /// <summary>Clears the Canvas and enqueues a snapshot event. Never throws.</summary>
    internal void CanvasClear()
    {
        try
        {
            lock (this._stateLock)
            {
                this.Canvas = CanvasState.Empty;
                this.BroadcastCanvas();
            }
        }
        catch
        {
            // Swallow.
        }
    }

    // -------------------------------------------------------------------------
    // Public eval entry
    // -------------------------------------------------------------------------

    /// <summary>
    /// Evaluates <paramref name="code"/> in the underlying session, serialized by the eval
    /// semaphore. Side effects (dump, canvas, console) occur during evaluation.
    /// </summary>
    /// <param name="code">The TypeScript/JavaScript code to evaluate.</param>
    /// <param name="appendResult">
    /// When <see langword="true"/> and the evaluation succeeds with a non-<c>undefined</c>
    /// result, renders the result and appends it to the Timeline with reason
    /// <c>"evaluation"</c> — exactly as <see cref="Dump"/> does. Pass <see langword="true"/>
    /// for the Immediate surface; leave <see langword="false"/> (the default) for Editor runs
    /// so that Editor evaluation results are not automatically added to the Timeline.
    /// </param>
    public async Task<EvalResult> EvaluateAsync(string code, bool appendResult = false)
    {
        await this._evalSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var value = this.DuetsSession.Evaluate(code);

            // After all in-eval side effects (dump, console, canvas) have run,
            // optionally append the final result to the Timeline.
            // Skip Undefined (e.g. void 0) and Null: void script operations such as
            // console.log(...) and canvas.add/set/clear(...) surface as ScriptValue.Null
            // in the Jint backend, and echoing them as an "evaluation" entry would be noise.
            if (appendResult && value != ScriptValue.Undefined && value != ScriptValue.Null)
            {
                this.AppendEvaluationResult(value.ToObject());
            }

            return new EvalResult(Ok: true, Result: value.ToString(), Error: null);
        }
        catch (Exception ex)
        {
            return new EvalResult(Ok: false, Result: null, Error: ex.Message);
        }
        finally
        {
            this._evalSemaphore.Release();
        }
    }

    /// <summary>
    /// Renders <paramref name="value"/> and appends a Timeline entry with reason
    /// <c>"evaluation"</c>. On render failure, appends an output-error marker instead.
    /// Never throws.
    /// </summary>
    private void AppendEvaluationResult(object? value)
    {
        try
        {
            ITerminalRenderNode body;
            try
            {
                body = this._pipeline.Render(value);
            }
            catch (Exception ex)
            {
                this.AppendTimelineEntry(
                    "render-error",
                    OutputError.Create($"Render error: {ex.Message}")
                );
                return;
            }

            this.AppendTimelineEntry("evaluation", body);
        }
        catch
        {
            // Absolute last resort: swallow so eval is never disrupted.
        }
    }

    // -------------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        this.DuetsSession.ConsoleLogged -= this.OnConsoleLogged;

        // Complete all subscriber channels so readers can drain.
        foreach (var (_, writer) in this._canvasSubscribers)
        {
            writer.TryComplete();
        }

        this._canvasSubscribers.Clear();

        foreach (var (_, writer) in this._timelineSubscribers)
        {
            writer.TryComplete();
        }

        this._timelineSubscribers.Clear();

        this.DuetsSession.Dispose();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void OnConsoleLogged(ScriptConsoleEntry entry)
    {
        try
        {
            var levelClass = entry.Level.ToString().ToLowerInvariant();
            var body = new Element(
                "div",
                new ElementAttributes(
                    new KeyValuePair<string, string?>(
                        "class",
                        $"duetspad-console duetspad-console-{levelClass}"
                    )
                ),
                new ElementChildren(new Text(entry.Text))
            );
            this.AppendTimelineEntry("console", body);
        }
        catch
        {
            // Swallow — must not disrupt the eval.
        }
    }

    /// <summary>
    /// Appends a Timeline entry and enqueues a timeline append event.
    /// Acquires <c>_stateLock</c> so enqueue happens in the same critical section as the
    /// state update, preserving ordering for late-joining subscribers.
    /// </summary>
    private void AppendTimelineEntry(string reason, ITerminalRenderNode body)
    {
        lock (this._stateLock)
        {
            this.Timeline = this.Timeline.Append(reason, body);
            var entry = this.Timeline[^1];
            this.BroadcastTimeline(TimelineEventMessage.Append(entry));
        }
    }

    /// <summary>
    /// Enqueues a <c>canvas.replace</c> event to all registered subscribers.
    /// Must be called while <c>_stateLock</c> is held.
    /// </summary>
    private void BroadcastCanvas()
    {
        var msg = CanvasEventMessage.Replace(this.Canvas);
        foreach (var (_, writer) in this._canvasSubscribers)
        {
            writer.TryWrite(msg);
        }
    }

    /// <summary>
    /// Enqueues <paramref name="msg"/> to all registered Timeline subscribers.
    /// Must be called while <c>_stateLock</c> is held.
    /// </summary>
    private void BroadcastTimeline(TimelineEventMessage msg)
    {
        foreach (var (_, writer) in this._timelineSubscribers)
        {
            writer.TryWrite(msg);
        }
    }
}
