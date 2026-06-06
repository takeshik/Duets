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
///
/// <para>
/// <b>Multi-browser note</b><br/>
/// Multiple simultaneous SSE subscribers (e.g. two browser tabs) are permitted at the
/// infrastructure level — all subscribers receive the same broadcast events. However,
/// first-class multi-browser session sharing is NOT supported: there is no lease mechanism,
/// and a DELETE or idle-eviction of the session affects all subscribers simultaneously.
/// </para>
/// </remarks>
internal sealed class DuetsPadSession : IDisposable
{
    // Serializes public eval entry; NOT re-acquired by internal ops.
    private readonly SemaphoreSlim _evalSemaphore = new(1, 1);

    // Guards state mutation and subscriber enqueue. Never held across I/O.
    private readonly object _stateLock = new();

    // 0 = live, 1 = disposed. Set (via Interlocked.Exchange) at the very start of Dispose, before
    // the eval semaphore is awaited, so an eval that begins after Dispose started observes it.
    // Subscriber registration reads this field under _stateLock; Dispose's subscriber complete/clear
    // also runs under _stateLock after the set, so the lock establishes the happens-before that
    // serializes registration against teardown (closing the TOCTOU window).
    private int _disposed;

    private readonly Func<DateTimeOffset> _clock;

    // Last-activity timestamp stored as UTC ticks; updated via Interlocked for lock-free reads.
    private long _lastActivityTicks;

    private readonly ConcurrentDictionary<
        Guid,
        ChannelWriter<CanvasEventMessage>
    > _canvasSubscribers = new();

    private readonly ConcurrentDictionary<
        Guid,
        ChannelWriter<TimelineEventMessage>
    > _timelineSubscribers = new();

    private readonly ConcurrentDictionary<
        Guid,
        ChannelWriter<TypeDeclaration?>
    > _typeDeclarationSubscribers = new();

    private ObjectRenderingPipeline _pipeline;

    private readonly int? _timelineEntryLimit;

    public DuetsPadSession(
        Guid id,
        DuetsSession duetsSession,
        IReadOnlyList<IObjectRenderer>? objectRenderers = null,
        Func<DateTimeOffset>? clock = null,
        int? timelineEntryLimit = null
    )
    {
        this.Id =
            id == Guid.Empty
                ? throw new ArgumentException("Session id cannot be empty.", nameof(id))
                : id;
        this.DuetsSession = duetsSession ?? throw new ArgumentNullException(nameof(duetsSession));
        this._clock = clock ?? (() => DateTimeOffset.UtcNow);
        this._timelineEntryLimit = timelineEntryLimit is null or > 0
            ? timelineEntryLimit
            : throw new ArgumentOutOfRangeException(
                nameof(timelineEntryLimit),
                "Timeline entry limit must be positive."
            );

        this.ObjectRenderers = objectRenderers is null ? [] : [.. objectRenderers];
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

        // Record creation as the first activity.
        this.Touch();
    }

    public Guid Id { get; }

    public DuetsSession DuetsSession { get; }

    public CanvasState Canvas { get; private set; } = CanvasState.Empty;

    public TimelineState Timeline { get; private set; } = TimelineState.Empty;

    public IReadOnlyList<IObjectRenderer> ObjectRenderers { get; private set; }

    /// <summary>
    /// UTC timestamp of the most recent session activity (creation, eval, or SSE attach/keepalive).
    /// Updated atomically via <c>Interlocked.Exchange</c>.
    /// </summary>
    internal DateTimeOffset LastActivityUtc =>
        new(Interlocked.Read(ref this._lastActivityTicks), TimeSpan.Zero);

    // -------------------------------------------------------------------------
    // Activity tracking
    // -------------------------------------------------------------------------

    /// <summary>
    /// Records the current clock time as the most recent session activity.
    /// Call on session creation, eval entry, SSE attach, and SSE keepalive.
    /// </summary>
    internal void Touch()
    {
        Interlocked.Exchange(ref this._lastActivityTicks, this._clock().UtcTicks);
    }

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
        if (objectRenderers is null)
        {
            throw new ArgumentNullException(nameof(objectRenderers));
        }

        this.ObjectRenderers = [.. objectRenderers];

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

        this.Touch();
        var key = Guid.NewGuid();
        lock (this._stateLock)
        {
            // The disposed-test and the insert are atomic with respect to Dispose's clear (which
            // runs under the same lock): either this registration completes before the clear (and
            // its writer is completed by the clear), or the clear runs first and this observes
            // _disposed == 1 and self-completes the writer. If disposed, completing the writer ends
            // the SSE read loop at once; its finally RemoveCanvasSubscriber(Guid.Empty) is a
            // harmless no-op.
            if (this._disposed == 1)
            {
                writer.TryComplete();
                return Guid.Empty;
            }

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

        this.Touch();
        var key = Guid.NewGuid();
        lock (this._stateLock)
        {
            // The disposed-test and the insert are atomic with respect to Dispose's clear (which
            // runs under the same lock): either this registration completes before the clear (and
            // its writer is completed by the clear), or the clear runs first and this observes
            // _disposed == 1 and self-completes the writer. If disposed, completing the writer ends
            // the SSE read loop at once; its finally RemoveTimelineSubscriber(Guid.Empty) is a
            // harmless no-op.
            if (this._disposed == 1)
            {
                writer.TryComplete();
                return Guid.Empty;
            }

            this._timelineSubscribers[key] = writer;
            writer.TryWrite(TimelineEventMessage.Reset(this.Timeline, "initial"));
        }

        return key;
    }

    /// <summary>Removes the Timeline subscriber identified by <paramref name="key"/>.</summary>
    public void RemoveTimelineSubscriber(Guid key) =>
        this._timelineSubscribers.TryRemove(key, out _);

    /// <summary>
    /// Registers a type-declaration SSE subscriber. The caller is responsible for
    /// enqueuing existing declarations before or after this call (the route already does
    /// this). Returns the registration key used to unregister via
    /// <see cref="RemoveTypeDeclarationSubscriber"/>.
    /// </summary>
    internal Guid AddTypeDeclarationSubscriber(ChannelWriter<TypeDeclaration?> writer)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        var key = Guid.NewGuid();
        lock (this._stateLock)
        {
            // The disposed-test and the insert are atomic with respect to Dispose's clear (which
            // runs under the same lock): either this registration completes before the clear (and
            // its writer is completed by the clear), or the clear runs first and this observes
            // _disposed == 1 and self-completes the writer. There is no initial-snapshot enqueue
            // here — the route enqueues existing declarations. If disposed, completing the writer
            // ends the SSE read loop at once; its finally RemoveTypeDeclarationSubscriber(Guid.Empty)
            // is a harmless no-op.
            if (this._disposed == 1)
            {
                writer.TryComplete();
                return Guid.Empty;
            }

            this._typeDeclarationSubscribers[key] = writer;
        }

        return key;
    }

    /// <summary>Removes the type-declaration subscriber identified by <paramref name="key"/>.</summary>
    internal void RemoveTypeDeclarationSubscriber(Guid key) =>
        this._typeDeclarationSubscribers.TryRemove(key, out _);

    /// <summary>
    /// Returns <see langword="true"/> when at least one SSE subscriber (Canvas, Timeline, or
    /// type-declaration) is currently registered. Used by the idle-eviction sweep to protect
    /// sessions with live browser connections regardless of last-activity timestamp.
    /// </summary>
    internal bool HasActiveSubscribers =>
        !this._canvasSubscribers.IsEmpty
        || !this._timelineSubscribers.IsEmpty
        || !this._typeDeclarationSubscribers.IsEmpty;

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
        this.Touch();

        // Dispose sets _disposed = 1 BEFORE it acquires (and then disposes) the eval semaphore.
        // Checking the flag before waiting means an eval that begins after Dispose started never
        // touches an already-disposed DuetsSession or an already-disposed semaphore. The narrow
        // race where Dispose disposes the semaphore between this check and the wait below is
        // covered by catching ObjectDisposedException and reporting the same disposed result.
        if (Volatile.Read(ref this._disposed) == 1)
        {
            return new EvalResult(Ok: false, Result: null, Error: "Session has been disposed.");
        }

        try
        {
            await this._evalSemaphore.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return new EvalResult(Ok: false, Result: null, Error: "Session has been disposed.");
        }

        try
        {
            // Re-check under the semaphore: an eval that acquired the semaphore just before
            // Dispose did must still reject rather than evaluate a torn-down session.
            if (Volatile.Read(ref this._disposed) == 1)
            {
                return new EvalResult(Ok: false, Result: null, Error: "Session has been disposed.");
            }

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
        // Idempotency: the first caller wins; any later Dispose is a no-op.
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
        {
            return;
        }

        // Acquire the eval semaphore synchronously to wait for any in-flight eval to finish
        // before tearing down the underlying DuetsSession. DuetsSession.Dispose() enters the
        // same single-operation guard that Evaluate uses and throws on concurrent entry, so it
        // must never run while an eval is active. _disposed is already set above, so an eval
        // that has not yet acquired the semaphore will reject instead of starting.
        //
        // This makes Dispose() block until the current eval completes. Evals are expected to be
        // short, and this is the same serialization the eval semaphore already imposes.
        // Dispose is only ever called from the service layer (DELETE handler, idle sweep, service
        // Dispose), never from the eval call stack, so this synchronous wait cannot deadlock.
        this._evalSemaphore.Wait();
        try
        {
            this.DuetsSession.ConsoleLogged -= this.OnConsoleLogged;

            // Complete + clear all subscriber channels under _stateLock so this teardown is
            // serialized against Add*Subscriber (which tests _disposed and inserts under the same
            // lock). _disposed was already set to 1 above; holding _stateLock here provides the
            // happens-before so any Add* either ran fully before this clear (and is completed by it)
            // or observes _disposed == 1 and self-completes its writer. The lock guards only the
            // dictionary complete/clear; DuetsSession.Dispose() runs outside it (never hold
            // _stateLock across an I/O / teardown boundary).
            lock (this._stateLock)
            {
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

                foreach (var (_, writer) in this._typeDeclarationSubscribers)
                {
                    writer.TryComplete();
                }

                this._typeDeclarationSubscribers.Clear();
            }

            this.DuetsSession.Dispose();
        }
        finally
        {
            this._evalSemaphore.Release();
            this._evalSemaphore.Dispose();
        }
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

            if (this._timelineEntryLimit is int max && this.Timeline.Count > max)
            {
                var removeBeforeId = this.Timeline[^max].Id;
                this.Timeline = this.Timeline.Trim(removeBeforeId);
                this.BroadcastTimeline(TimelineEventMessage.Trim(removeBeforeId, marker: null));
            }
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
