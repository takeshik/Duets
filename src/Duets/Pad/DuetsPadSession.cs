using System.Collections.Concurrent;
using System.Threading.Channels;
using Duets.Pad.Interactions;
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
/// Canvas state is maintained per named canvas in a dictionary; the <c>"default"</c> canvas is
/// always present. The <see cref="Canvas"/> property returns <c>this</c> typed as
/// <see cref="ICanvasSurface"/> and routes to the <c>"default"</c> canvas. Named-canvas
/// access for future multi-canvas support uses the internal <c>CanvasAdd</c>/<c>CanvasSet</c>/
/// <c>CanvasClear</c> methods directly. Timeline is exposed via <see cref="Timeline"/>.
/// Unified SSE subscription is managed via <see cref="SubscribeEvents"/> and
/// <see cref="UnsubscribeEvents"/>.
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
/// Internal ops — <see cref="Dump"/>, and the canvas interface members Add/Set/Clear —
/// are called synchronously <em>from within</em> the eval call stack. They therefore MUST NOT
/// re-acquire <c>_evalSemaphore</c> (deadlock). They share a separate <c>_stateLock</c>
/// object that is also held by subscriber registration. This is the <em>only</em> lock used
/// for state mutation + event dispatch, and it is never held across an I/O boundary.
/// </para>
///
/// <para>
/// <b>Initial-event ordering</b><br/>
/// When a new SSE subscriber connects, the initial event must reflect a state that is at least
/// as new as any update the subscriber could subsequently receive. To guarantee this without a
/// TOCTOU gap, <see cref="SubscribeEvents"/> acquires <c>_stateLock</c> and, <em>while still
/// holding it</em>, both registers the writer and immediately enqueues the current-state initial
/// events (<c>canvas.snapshot</c>, <c>timeline.reset</c>, and any existing type declarations)
/// to that writer. Subsequent mutations enqueue to all registered writers under the same lock.
/// As a result a subscriber either (a) registers before a mutation and therefore sees the
/// initial events followed by the update, or (b) registers after the mutation and sees the
/// post-mutation initial events — neither can observe the post-mutation state first.
/// </para>
///
/// <para>
/// All enqueues use <see cref="ChannelWriter{T}.TryWrite"/> (non-blocking). A slow or
/// disconnected subscriber is silently dropped if its channel is full; it should be removed
/// and its channel completed via <see cref="UnsubscribeEvents"/>.
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
internal sealed class DuetsPadSession : IDisposable, ICanvasSurface, ITimelineSurface
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

    private readonly ConcurrentDictionary<Guid, ChannelWriter<PadEventMessage?>> _eventSubscribers =
        new();

    // Pending control commands queued during eval; flushed after eval/handler completes.
    // Accessed only under _stateLock.
    private readonly List<PadEventMessage.Control> _pendingControl = [];

    private readonly DisplayRenderer _renderer;
    private readonly InteractionStore _interactionStore = new();

    private readonly int? _timelineEntryLimit;

    public DuetsPadSession(
        Guid id,
        DuetsSession duetsSession,
        IReadOnlyList<IObjectRenderer>? objectRenderers = null,
        Func<DateTimeOffset>? clock = null,
        int? timelineEntryLimit = null,
        DumpOptions? dumpOptions = null
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
        this._renderer = new DisplayRenderer(this.ObjectRenderers);
        this.DumpOptions = dumpOptions ?? DumpOptions.Default;

        // Wire the JS environment: console/dump/canvas/ui globals and per-session .d.ts declarations.
        SessionBootstrap.Bootstrap(this, this._renderer);

        // Forward new type declarations to all unified-event subscribers.
        this.DuetsSession.Declarations.DeclarationChanged += this.OnDeclarationChanged;

        // Record creation as the first activity.
        this.Touch();
    }

    public Guid Id { get; }

    public DuetsSession DuetsSession { get; }

    /// <summary>
    /// Grouped canvas sub-API for the default canvas. Routes to the <c>"default"</c> canvas name.
    /// </summary>
    internal ICanvasSurface Canvas => this;

    /// <summary>Grouped timeline sub-API: state snapshot.</summary>
    internal ITimelineSurface Timeline => this;

    // Backing state fields; accessed directly by internal methods and returned via interface State getters.
    // Canvas state is keyed by name; the "default" canvas is always present.
    private readonly Dictionary<string, CanvasState> _canvasStates = new(StringComparer.Ordinal)
    {
        ["default"] = CanvasState.Empty,
    };
    private TimelineState _timelineState = TimelineState.Empty;

    public IReadOnlyList<IObjectRenderer> ObjectRenderers { get; }

    /// <summary>
    /// Session-default <see cref="Rendering.DumpOptions" /> applied to all render entry points
    /// (<c>dump</c>, <c>canvas</c>, <c>ui</c>). The <c>dump(value, opts?)</c> function accepts
    /// a per-call override merged over this value via <see cref="DumpOptionsResolver.Merge"/>.
    /// </summary>
    public DumpOptions DumpOptions { get; private set; }

    /// <summary>
    /// UTC timestamp of the most recent session activity (creation, eval, or SSE attach/keepalive).
    /// Updated atomically via <c>Interlocked.Exchange</c>.
    /// </summary>
    internal DateTimeOffset LastActivityUtc =>
        new(Interlocked.Read(ref this._lastActivityTicks), TimeSpan.Zero);

    // Activity tracking

    /// <summary>
    /// Records the current clock time as the most recent session activity.
    /// Call on session creation, eval entry, SSE attach, and SSE keepalive.
    /// </summary>
    internal void Touch()
    {
        Interlocked.Exchange(ref this._lastActivityTicks, this._clock().UtcTicks);
    }

    /// <summary>
    /// Returns <see langword="true"/> when at least one SSE subscriber is currently registered.
    /// Used by the idle-eviction sweep to protect sessions with live browser connections
    /// regardless of last-activity timestamp.
    /// </summary>
    internal bool HasActiveSubscribers => !this._eventSubscribers.IsEmpty;

    // Explicit ICanvasSurface implementation — routes to the "default" canvas

    string ICanvasSurface.Name => "default";

    CanvasState ICanvasSurface.State => this._canvasStates["default"];

    void ICanvasSurface.Add(object? value) => this.CanvasAdd("default", value);

    void ICanvasSurface.Set(object? value) => this.CanvasSet("default", value);

    void ICanvasSurface.Clear() => this.CanvasClear("default");

    // Internal name-aware canvas mutation methods

    /// <summary>
    /// Renders <paramref name="value"/> and appends it to the canvas named <paramref name="name"/>.
    /// Creates the canvas if it does not yet exist. Never throws.
    /// </summary>
    internal void CanvasAdd(string name, object? value)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("The argument cannot be null or empty.", nameof(name));
        }

        try
        {
            var (content, isError) = this.TryRenderContent(value, this.DumpOptions);
            if (isError)
            {
                this.AppendTimelineEntry("render-error", content);
                return;
            }

            lock (this._stateLock)
            {
                if (!this._canvasStates.TryGetValue(name, out var state))
                {
                    state = CanvasState.Empty;
                }

                var childIndex = state.Root.Children.Count;
                this._canvasStates[name] = state.Append(content.Body);
                this._interactionStore.AppendCanvasInteractions(
                    name,
                    content.Interactions,
                    childIndex
                );
                this.BroadcastCanvas(name);
            }
        }
        catch
        {
            // Swallow.
        }
    }

    /// <summary>
    /// Renders <paramref name="value"/> and replaces the entire canvas named <paramref name="name"/>
    /// with it. Creates the canvas if it does not yet exist. Never throws.
    /// </summary>
    internal void CanvasSet(string name, object? value)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("The argument cannot be null or empty.", nameof(name));
        }

        try
        {
            var (content, isError) = this.TryRenderContent(value, this.DumpOptions);
            if (isError)
            {
                this.AppendTimelineEntry("render-error", content);
                return;
            }

            lock (this._stateLock)
            {
                if (!this._canvasStates.TryGetValue(name, out var state))
                {
                    state = CanvasState.Empty;
                }

                this._canvasStates[name] = state.Set(new ElementChildren(content.Body));
                this._interactionStore.SetCanvasInteractions(
                    name,
                    content.Interactions,
                    childIndex: 0
                );
                this.BroadcastCanvas(name);
            }
        }
        catch
        {
            // Swallow.
        }
    }

    /// <summary>
    /// Clears the canvas named <paramref name="name"/> and enqueues a snapshot event.
    /// Creates the canvas if it does not yet exist. Never throws.
    /// </summary>
    internal void CanvasClear(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("The argument cannot be null or empty.", nameof(name));
        }

        try
        {
            lock (this._stateLock)
            {
                this._canvasStates[name] = CanvasState.Empty;
                this._interactionStore.ClearCanvasInteractions(name);
                this.BroadcastCanvas(name);
            }
        }
        catch
        {
            // Swallow.
        }
    }

    // Explicit ITimelineSurface implementation

    TimelineState ITimelineSurface.State => this._timelineState;

    // Internal ops — called from the eval call stack; must not acquire _evalSemaphore

    /// <summary>
    /// Renders <paramref name="value"/> using the given <paramref name="options"/> and appends a
    /// Timeline entry with reason "dump". On render failure, appends an output-error marker entry
    /// instead. Never throws.
    /// </summary>
    internal void Dump(object? value, DumpOptions options)
    {
        try
        {
            var (content, isError) = this.TryRenderContent(value, options);
            this.AppendTimelineEntry(isError ? "render-error" : "dump", content);
        }
        catch
        {
            // Absolute last resort: swallow so eval is never disrupted.
        }
    }

    // Public eval entry

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
            // Flush any control commands queued during eval before releasing the semaphore,
            // so subscribers receive them in the same logical "turn" as the eval output.
            this.FlushPendingControl();
            this._evalSemaphore.Release();
        }
    }

    internal async Task<InteractionInvokeResult> InvokeInteractionAsync(Guid handlerId)
    {
        this.Touch();

        if (handlerId == Guid.Empty)
        {
            return InteractionInvokeResult.StaleHandler("Interaction handler id cannot be empty.");
        }

        if (Volatile.Read(ref this._disposed) == 1)
        {
            return InteractionInvokeResult.StaleHandler("Session has been disposed.");
        }

        try
        {
            await this._evalSemaphore.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return InteractionInvokeResult.StaleHandler("Session has been disposed.");
        }

        try
        {
            if (Volatile.Read(ref this._disposed) == 1)
            {
                return InteractionInvokeResult.StaleHandler("Session has been disposed.");
            }

            Action? handler;
            lock (this._stateLock)
            {
                this._interactionStore.TryGetHandler(handlerId, out handler);
            }

            if (handler is null)
            {
                const string error = "Interaction handler is no longer available.";
                this.AppendTimelineEntry(
                    "handler-error",
                    DisplayContent.FromNode(OutputError.Create(error))
                );
                return InteractionInvokeResult.StaleHandler(error);
            }

            try
            {
                handler();
                return InteractionInvokeResult.Success;
            }
            catch (Exception ex)
            {
                var error = $"Handler error: {ex.Message}";
                this.AppendTimelineEntry(
                    "handler-error",
                    DisplayContent.FromNode(OutputError.Create(error))
                );
                return InteractionInvokeResult.Failed(error);
            }
        }
        finally
        {
            // Flush any control commands queued by the handler before releasing the semaphore.
            this.FlushPendingControl();
            this._evalSemaphore.Release();
        }
    }

    /// <summary>
    /// Queues a control command to be broadcast to all subscribers after the current eval or
    /// interaction handler completes. Must be called from within the eval call stack (i.e. while
    /// <c>_evalSemaphore</c> is held). The command is not broadcast immediately; it is held in
    /// <c>_pendingControl</c> and flushed by <see cref="FlushPendingControl"/> once all eval
    /// side effects have run.
    /// </summary>
    /// <param name="op">The operation name, without the <c>control.</c> prefix.</param>
    /// <param name="payload">Arbitrary key-value payload to attach to the command.</param>
    /// <param name="replace">
    /// When <see langword="true"/>, any existing pending entry with the same <paramref name="op"/>
    /// is replaced by this one (last-wins collapse). When <see langword="false"/> (the default),
    /// the entry is appended unconditionally.
    /// </param>
    internal void EnqueueControl(
        string op,
        IReadOnlyDictionary<string, object?> payload,
        bool replace = false
    )
    {
        lock (this._stateLock)
        {
            if (replace)
            {
                var existing = this._pendingControl.FindIndex(c => c.Op == op);
                if (existing >= 0)
                {
                    this._pendingControl[existing] = new PadEventMessage.Control(op, payload);
                    return;
                }
            }

            this._pendingControl.Add(new PadEventMessage.Control(op, payload));
        }
    }

    /// <summary>
    /// Enqueues a <c>reset</c> control command with last-wins collapse semantics.
    /// Multiple calls within the same eval are coalesced into a single command.
    /// </summary>
    internal void RequestResetSession()
    {
        this.EnqueueControl(
            ControlEventTypes.Reset,
            new Dictionary<string, object?>(),
            replace: true
        );
    }

    /// <summary>
    /// Enqueues an <c>openText</c> control command. Every call is delivered; there is no collapse.
    /// </summary>
    /// <param name="text">The text to hand off as the initial content of the new tab.</param>
    internal void RequestOpenText(string text)
    {
        this.EnqueueControl(
            ControlEventTypes.OpenText,
            new Dictionary<string, object?> { ["text"] = text }
        );
    }

    /// <summary>
    /// Enqueues a <c>setEditorText</c> control command with last-wins collapse semantics.
    /// Multiple calls within the same eval retain only the last value.
    /// </summary>
    /// <param name="text">The text to place in the editor.</param>
    internal void RequestSetEditorText(string text)
    {
        this.EnqueueControl(
            ControlEventTypes.SetEditorText,
            new Dictionary<string, object?> { ["text"] = text },
            replace: true
        );
    }

    /// <summary>
    /// Broadcasts all pending control commands to subscribers in the order they were enqueued,
    /// then clears the pending list. Called after eval or interaction-handler completion (in
    /// <c>finally</c>) so commands are sent regardless of success or failure.
    /// Must NOT be called while <c>_stateLock</c> is already held.
    /// </summary>
    private void FlushPendingControl()
    {
        lock (this._stateLock)
        {
            if (this._pendingControl.Count == 0)
            {
                return;
            }

            foreach (var ctrl in this._pendingControl)
            {
                this.BroadcastControl(ctrl);
            }

            this._pendingControl.Clear();
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
            var (content, isError) = this.TryRenderContent(value, this.DumpOptions);
            this.AppendTimelineEntry(isError ? "render-error" : "evaluation", content);
        }
        catch
        {
            // Absolute last resort: swallow so eval is never disrupted.
        }
    }

    // Dispose

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
            this.DuetsSession.Declarations.DeclarationChanged -= this.OnDeclarationChanged;

            // Complete + clear all subscriber channels under _stateLock so this teardown is
            // serialized against Subscribe calls (which test _disposed and insert under the same
            // lock). _disposed was already set to 1 above; holding _stateLock here provides the
            // happens-before so any Subscribe either ran fully before this clear (and is completed
            // by it) or observes _disposed == 1 and self-completes its writer. The lock guards
            // only the dictionary complete/clear; DuetsSession.Dispose() runs outside it (never
            // hold _stateLock across an I/O / teardown boundary).
            lock (this._stateLock)
            {
                // Complete all subscriber channels so readers can drain.
                foreach (var (_, writer) in this._eventSubscribers)
                {
                    writer.TryComplete();
                }

                this._eventSubscribers.Clear();

                this._canvasStates.Clear();
                this._interactionStore.Clear();
            }

            this.DuetsSession.Dispose();
        }
        finally
        {
            this._evalSemaphore.Release();
            this._evalSemaphore.Dispose();
        }
    }

    // Private helpers

    /// <summary>
    /// Renders <paramref name="value"/> to a <see cref="DisplayContent"/>. Never throws.
    /// </summary>
    /// <returns>
    /// A tuple of (<c>Content</c>, <c>IsRenderError</c>). When <c>IsRenderError</c> is
    /// <see langword="false"/>, <c>Content</c> is the successfully rendered result. When
    /// <c>IsRenderError</c> is <see langword="true"/>, <c>Content</c> contains an
    /// <c>OutputError</c> node describing the failure; the caller should append a
    /// <c>render-error</c> Timeline entry (under <c>_stateLock</c>) and return.
    /// </returns>
    /// <remarks>
    /// The render step is performed OUTSIDE <c>_stateLock</c> by design. The P3 invariant
    /// requires that the initial SSE event be enqueued under the same lock that guards
    /// subsequent mutations; callers therefore must acquire <c>_stateLock</c> themselves
    /// before appending the returned content or broadcasting state changes.
    /// </remarks>
    private (DisplayContent Content, bool IsRenderError) TryRenderContent(
        object? value,
        DumpOptions options
    )
    {
        try
        {
            return (this._renderer.Render(value, options), false);
        }
        catch (Exception ex)
        {
            var errorContent = DisplayContent.FromNode(
                OutputError.Create($"Render error: {ex.Message}")
            );
            return (errorContent, true);
        }
    }

    /// <summary>
    /// Forwards a new type declaration to all registered unified-event subscribers.
    /// Called by the <c>DeclarationChanged</c> event. Never throws.
    /// </summary>
    internal void OnDeclarationChanged(TypeDeclaration decl)
    {
        try
        {
            lock (this._stateLock)
            {
                this.BroadcastTypeDeclaration(decl);
            }
        }
        catch
        {
            // Swallow — must not disrupt the eval.
        }
    }

    internal void OnConsoleLogged(ScriptConsoleEntry entry)
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
            var (content, isError) = this.TryRenderContent(body, this.DumpOptions);
            this.AppendTimelineEntry(isError ? "render-error" : "console", content);
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
    private void AppendTimelineEntry(string reason, DisplayContent content)
    {
        lock (this._stateLock)
        {
            this._timelineState = this._timelineState.Append(reason, content.Body, this._clock());
            var entry = this._timelineState[^1];
            var interactions = this._interactionStore.CommitTimelineInteractions(
                entry.Id,
                content.Interactions
            );

            this.BroadcastTimeline(TimelineEventMessage.Append(entry, interactions));

            if (this._timelineEntryLimit is int max && this._timelineState.Count > max)
            {
                var (trimmedTimeline, removeBeforeId, removedIds) = this._timelineState.TrimToLimit(
                    max
                );
                this._interactionStore.DiscardTimelineInteractions(removedIds);
                this._timelineState = trimmedTimeline;
                this.BroadcastTimeline(TimelineEventMessage.Trim(removeBeforeId, marker: null));
            }
        }
    }

    /// <summary>
    /// Enqueues a <c>canvas.replace</c> event for the canvas named <paramref name="name"/> to all
    /// registered subscribers. Must be called while <c>_stateLock</c> is held.
    /// </summary>
    private void BroadcastCanvas(string name)
    {
        var state = this._canvasStates.TryGetValue(name, out var s) ? s : CanvasState.Empty;
        var msg = CanvasEventMessage.Replace(
            name,
            state,
            this._interactionStore.GetCanvasInteractions(name)
        );
        var padMsg = new PadEventMessage.Canvas(msg);
        foreach (var (_, writer) in this._eventSubscribers)
        {
            writer.TryWrite(padMsg);
        }
    }

    /// <summary>
    /// Enqueues <paramref name="msg"/> to all registered Timeline subscribers.
    /// Must be called while <c>_stateLock</c> is held.
    /// </summary>
    private void BroadcastTimeline(TimelineEventMessage msg)
    {
        var padMsg = new PadEventMessage.Timeline(msg);
        foreach (var (_, writer) in this._eventSubscribers)
        {
            writer.TryWrite(padMsg);
        }
    }

    /// <summary>
    /// Enqueues <paramref name="ctrl"/> as a <c>control.*</c> event to all registered
    /// unified-event subscribers. Must be called while <c>_stateLock</c> is held.
    /// </summary>
    private void BroadcastControl(PadEventMessage.Control ctrl)
    {
        foreach (var (_, writer) in this._eventSubscribers)
        {
            writer.TryWrite(ctrl);
        }
    }

    /// <summary>
    /// Enqueues <paramref name="decl"/> as a <c>type.declaration</c> event to all registered
    /// unified-event subscribers. Must be called while <c>_stateLock</c> is held.
    /// </summary>
    private void BroadcastTypeDeclaration(TypeDeclaration decl)
    {
        var padMsg = new PadEventMessage.TypeDeclaration(decl);
        foreach (var (_, writer) in this._eventSubscribers)
        {
            writer.TryWrite(padMsg);
        }
    }

    /// <summary>
    /// Registers a unified SSE subscriber that receives canvas, timeline, and type-declaration
    /// events on a single channel. The initial snapshot is enqueued under <c>_stateLock</c> in
    /// the order: <c>canvas.snapshot</c> → <c>timeline.reset</c> → <c>type.declaration</c>
    /// (one per registered declaration).
    /// </summary>
    /// <param name="writer">The channel writer to receive <see cref="PadEventMessage"/> items.</param>
    /// <param name="declarations">
    /// The session's declaration collection, used to enqueue initial type declarations.
    /// </param>
    /// <returns>The registration key used to unregister via <see cref="UnsubscribeEvents"/>.</returns>
    internal Guid SubscribeEvents(
        ChannelWriter<PadEventMessage?> writer,
        ITypeDeclarationProvider declarations
    )
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (declarations is null)
        {
            throw new ArgumentNullException(nameof(declarations));
        }

        this.Touch();
        var key = Guid.NewGuid();
        lock (this._stateLock)
        {
            // The disposed-test and the insert are atomic with respect to Dispose's clear (which
            // runs under the same lock): either this registration completes before the clear (and
            // its writer is completed by the clear), or the clear runs first and this observes
            // _disposed == 1 and self-completes the writer. If disposed, completing the writer ends
            // the SSE read loop at once; its finally UnsubscribeEvents(Guid.Empty) is a harmless no-op.
            if (this._disposed == 1)
            {
                writer.TryComplete();
                return Guid.Empty;
            }

            this._eventSubscribers[key] = writer;

            // Initial snapshot order: canvas.snapshot (one per canvas) → timeline.reset → type.declaration(s).
            foreach (var (canvasName, canvasState) in this._canvasStates)
            {
                writer.TryWrite(
                    new PadEventMessage.Canvas(
                        CanvasEventMessage.Snapshot(
                            canvasName,
                            canvasState,
                            this._interactionStore.GetCanvasInteractions(canvasName)
                        )
                    )
                );
            }

            writer.TryWrite(
                new PadEventMessage.Timeline(
                    TimelineEventMessage.Reset(
                        this._timelineState,
                        "initial",
                        this._interactionStore.TimelineInteractions
                    )
                )
            );

            foreach (var decl in declarations.GetDeclarations())
            {
                writer.TryWrite(new PadEventMessage.TypeDeclaration(decl));
            }
        }

        return key;
    }

    /// <summary>
    /// Removes the unified event subscriber identified by <paramref name="key"/>.
    /// </summary>
    internal void UnsubscribeEvents(Guid key) => this._eventSubscribers.TryRemove(key, out _);
}
