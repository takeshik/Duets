using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Duets.Completions;
using Duets.Pad.Attachments;
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
/// for state mutation + event dispatch, and it is never held across an I/O boundary. Reachability
/// pruning may acquire the attachment-store lock while holding <c>_stateLock</c>; the attachment
/// store must therefore never call back into session state while holding its own lock.
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
internal sealed class DuetsPadSession
    : IDisposable,
        ICanvasSurface,
        ITimelineSurface,
        ISlotHost,
        IFieldHost,
        IFilePickerHost,
        IToastHost
{
    private sealed record CanvasProjection(CanvasState State, long Revision)
    {
        public static CanvasProjection Empty { get; } = new(CanvasState.Empty, Revision: 0);
    }

    // Serializes public eval entry; NOT re-acquired by internal ops.
    private readonly SemaphoreSlim _evalSemaphore = new(1, 1);

    // Serializes tagged-template completion callbacks independently from eval.
    private readonly SemaphoreSlim _completionSemaphore = new(1, 1);

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
    private readonly FieldStore _fieldStore = new();
    private readonly AttachmentStore _attachmentStore;
    private readonly Guid _directAttachmentClientId = Guid.NewGuid();
    private long _directAttachmentSequence;

    private readonly int? _timelineEntryLimit;
    private readonly bool _taggedTemplateSnapshotsEnabled;
    private readonly int _taggedTemplateCompletionRateLimitPerSecond;
    private readonly TimeSpan _taggedTemplateCompletionTimeout;
    private readonly object _completionLock = new();
    private readonly Queue<DateTimeOffset> _completionRequestTimes = new();
    private CancellationTokenSource? _activeCompletionCancellation;

    public DuetsPadSession(
        Guid id,
        DuetsSession duetsSession,
        IReadOnlyList<IObjectRenderer>? objectRenderers = null,
        Func<DateTimeOffset>? clock = null,
        int? timelineEntryLimit = null,
        DumpOptions? dumpOptions = null,
        bool taggedTemplateSnapshotsEnabled = true,
        int taggedTemplateCompletionRateLimitPerSecond = 30,
        TimeSpan? taggedTemplateCompletionTimeout = null,
        IAttachmentStorage? attachmentStorage = null,
        long maxAttachmentBytesPerFile = DuetsPadServiceOptions.DefaultMaxAttachmentBytesPerFile,
        long maxAttachmentBytesPerSession =
            DuetsPadServiceOptions.DefaultMaxAttachmentBytesPerSession,
        int maxAttachmentsPerSession = DuetsPadServiceOptions.DefaultMaxAttachmentsPerSession,
        TimeSpan? attachmentStorageDrainTimeout = null
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
        this._taggedTemplateSnapshotsEnabled = taggedTemplateSnapshotsEnabled;
        this._taggedTemplateCompletionRateLimitPerSecond =
            taggedTemplateCompletionRateLimitPerSecond > 0
                ? taggedTemplateCompletionRateLimitPerSecond
                : throw new ArgumentOutOfRangeException(
                    nameof(taggedTemplateCompletionRateLimitPerSecond),
                    "Tagged-template completion rate limit must be positive."
                );
        this._taggedTemplateCompletionTimeout =
            taggedTemplateCompletionTimeout is { } timeout && timeout > TimeSpan.Zero
                ? timeout
                : TimeSpan.FromSeconds(2);

        this.ObjectRenderers = objectRenderers is null ? [] : [.. objectRenderers];
        this._renderer = new DisplayRenderer(this.ObjectRenderers);
        this.DumpOptions = dumpOptions ?? DumpOptions.Default;
        this._attachmentStore = new AttachmentStore(
            attachmentStorage
                ?? new TemporaryFileAttachmentStorage(new AttachmentStorageContext(this.Id)),
            maxAttachmentBytesPerFile,
            maxAttachmentBytesPerSession,
            maxAttachmentsPerSession,
            attachmentStorageDrainTimeout
                ?? DuetsPadServiceOptions.DefaultAttachmentStorageDrainTimeout
        );

        // Wire the JS environment: console/dump/canvas/ui globals and per-session .d.ts declarations.
        SessionBootstrap.Bootstrap(this, this._renderer);

        // Forward new type declarations to all unified-event subscribers.
        this.DuetsSession.Declarations.DeclarationChanged += this.OnDeclarationChanged;
        if (this._taggedTemplateSnapshotsEnabled)
        {
            this.DuetsSession.TaggedTemplates.Changed += this.OnTaggedTemplateRegistryChanged;
        }

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

    private readonly CanvasDiffer _canvasDiffer = new();

    // Backing state fields; accessed directly by internal methods and returned via interface State getters.
    // Canvas projections are keyed by name; the "default" canvas is always present.
    private readonly Dictionary<string, CanvasProjection> _canvasProjections = new(
        StringComparer.Ordinal
    )
    {
        ["default"] = new(CanvasState.Empty, Revision: 0),
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

    CanvasState ICanvasSurface.State => this._canvasProjections["default"].State;

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
                var existed = this._canvasProjections.TryGetValue(name, out var projection);
                projection ??= CanvasProjection.Empty;

                var childIndex = projection.State.Root.Children.Count;
                var newState = projection.State.Append(content.Body);
                ValidateCanvasInteractions(
                    newState,
                    this._interactionStore.GetCanvasInteractions(name),
                    content.Interactions,
                    childIndex
                );

                var revision = projection.Revision + 1;
                var interactions = this._interactionStore.PrepareAppendCanvasInteractions(
                    name,
                    content.Interactions,
                    childIndex
                );
                this.CommitCanvasMutation(
                    name,
                    existed,
                    projection,
                    newState,
                    revision,
                    interactions
                );
            }
        }
        catch (Exception ex)
        {
            this.AppendCanvasProjectionError(ex);
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
                var existed = this._canvasProjections.TryGetValue(name, out var projection);
                projection ??= CanvasProjection.Empty;

                var newState = projection.State.Set(new ElementChildren(content.Body));
                ValidateCanvasInteractions(newState, [], content.Interactions, childIndex: 0);

                if (
                    existed
                    && projection.State.Equals(newState)
                    && CanvasInteractionsEqual(
                        this._interactionStore.GetCanvasInteractions(name),
                        content.Interactions,
                        childIndex: 0
                    )
                )
                {
                    return;
                }

                var revision = projection.Revision + 1;
                var interactions = this._interactionStore.PrepareSetCanvasInteractions(
                    name,
                    content.Interactions,
                    childIndex: 0
                );
                this.CommitCanvasMutation(
                    name,
                    existed,
                    projection,
                    newState,
                    revision,
                    interactions
                );
            }
        }
        catch (Exception ex)
        {
            this.AppendCanvasProjectionError(ex);
        }
    }

    /// <summary>
    /// Clears the canvas named <paramref name="name"/> and enqueues a Canvas mutation event.
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
                var existed = this._canvasProjections.TryGetValue(name, out var projection);
                projection ??= CanvasProjection.Empty;

                if (
                    existed
                    && projection.State.Equals(CanvasState.Empty)
                    && this._interactionStore.GetCanvasInteractions(name).Count == 0
                )
                {
                    return;
                }

                var revision = projection.Revision + 1;
                var interactions = this._interactionStore.PrepareClearCanvasInteractions(name);
                this.CommitCanvasMutation(
                    name,
                    existed,
                    projection,
                    CanvasState.Empty,
                    revision,
                    interactions
                );
            }
        }
        catch (Exception ex)
        {
            this.AppendCanvasProjectionError(ex);
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

    /// <summary>
    /// Re-renders <paramref name="slot"/>'s current content and updates every Canvas and Timeline
    /// location where the slot is currently placed. Called synchronously from the eval call stack
    /// when script assigns <c>slot.content</c>; must not acquire <c>_evalSemaphore</c>. Never throws.
    /// </summary>
    void ISlotHost.UpdateSlot(DisplaySlot slot)
    {
        if (slot is null)
        {
            return;
        }

        try
        {
            // An unplaced slot (assignment before canvas.add/dump) has nothing to project: the new
            // value is already recorded on the handle. Skip rendering entirely so an unplaced
            // assignment cannot do wasted work or surface a spurious render-error.
            lock (this._stateLock)
            {
                if (!this.IsSlotPlaced(slot.Id))
                {
                    return;
                }
            }

            var (content, isError) = this.TryRenderContent(slot.Content, this.DumpOptions);
            if (isError)
            {
                this.AppendTimelineEntry("render-error", content);
                return;
            }

            lock (this._stateLock)
            {
                this.UpdateSlotInCanvases(slot.Id, content);
                this.UpdateSlotInTimeline(slot.Id, content);
                this.PruneFieldBackedState();
            }
        }
        catch (InvalidOperationException ex)
        {
            this.AppendCanvasProjectionError(ex);
        }
        catch
        {
            // Swallow — a slot update must never disrupt the eval.
        }
    }

    /// <summary>
    /// Replaces the marked subtree for <paramref name="slotId"/> in every canvas that currently
    /// contains it and broadcasts the resulting mutation. Must be called while <c>_stateLock</c>
    /// is held.
    /// </summary>
    private void UpdateSlotInCanvases(Guid slotId, DisplayContent content)
    {
        foreach (var name in this._canvasProjections.Keys.ToList())
        {
            var projection = this._canvasProjections[name];
            var markers = SlotMarker.Find(projection.State.Root, slotId);
            if (markers.Count == 0)
            {
                continue;
            }

            var newRoot = projection.State.Root;
            foreach (var markerPath in markers)
            {
                newRoot = (Element)SlotMarker.ReplaceContent(newRoot, markerPath, content.Body);
            }

            var newState = new CanvasState(newRoot);
            if (newState.Equals(projection.State) && content.Interactions.Count == 0)
            {
                // Identical non-interactive content: a true no-op. Skip so reassigning the same
                // value does not emit a phantom empty-operation patch or advance the revision.
                continue;
            }

            var plan = this._interactionStore.PrepareReplaceCanvasSlots(
                name,
                [.. markers.Select(m => new SlotInteractionReplacement(m, content.Interactions))]
            );
            this.CommitCanvasMutation(
                name,
                existed: true,
                projection,
                newState,
                projection.Revision + 1,
                plan
            );
        }
    }

    /// <summary>
    /// Replaces the marked subtree for <paramref name="slotId"/> in every Timeline entry that
    /// currently contains it and broadcasts a <c>timeline.update</c> for each. Must be called while
    /// <c>_stateLock</c> is held.
    /// </summary>
    private void UpdateSlotInTimeline(Guid slotId, DisplayContent content)
    {
        for (var i = 0; i < this._timelineState.Count; i++)
        {
            var entry = this._timelineState[i];
            var markers = SlotMarker.Find(entry.Body, slotId);
            if (markers.Count == 0)
            {
                continue;
            }

            var newBody = entry.Body;
            foreach (var markerPath in markers)
            {
                newBody = SlotMarker.ReplaceContent(newBody, markerPath, content.Body);
            }

            if (newBody.Equals(entry.Body) && content.Interactions.Count == 0)
            {
                // Identical non-interactive content: skip the redundant timeline.update.
                continue;
            }

            var newEntry = new TimelineEntry(entry.Id, entry.Reason, newBody, entry.Timestamp);
            this._timelineState = this._timelineState.Replace(newEntry);
            var interactions = this._interactionStore.ReplaceTimelineSlots(
                entry.Id,
                [.. markers.Select(m => new SlotInteractionReplacement(m, content.Interactions))]
            );
            this.BroadcastTimeline(TimelineEventMessage.Update(newEntry, interactions));
        }
    }

    /// <summary>
    /// Returns whether the slot identified by <paramref name="slotId"/> currently has at least one
    /// marker placement in any canvas projection or Timeline entry. Must be called while
    /// <c>_stateLock</c> is held.
    /// </summary>
    private bool IsSlotPlaced(Guid slotId)
    {
        foreach (var projection in this._canvasProjections.Values)
        {
            if (SlotMarker.Find(projection.State.Root, slotId).Count > 0)
            {
                return true;
            }
        }

        foreach (var entry in this._timelineState)
        {
            if (SlotMarker.Find(entry.Body, slotId).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    // IFieldHost — form-input field store (ADR-47)

    /// <summary>
    /// Returns the current stored value for <paramref name="fieldId"/>, or <c>""</c> when no value
    /// has been stored yet. Readable from any eval while the field's marker lives (ADR-47).
    /// </summary>
    string IFieldHost.GetFieldValue(Guid fieldId)
    {
        lock (this._stateLock)
        {
            return this._fieldStore.GetValue(fieldId);
        }
    }

    /// <summary>
    /// Returns whether a value has been stored for <paramref name="fieldId"/>, distinguishing
    /// "never stored" from "stored as the empty string" (ADR-47).
    /// </summary>
    bool IFieldHost.TryGetFieldValue(Guid fieldId, out string value)
    {
        lock (this._stateLock)
        {
            return this._fieldStore.TryGetValue(fieldId, out value);
        }
    }

    /// <summary>
    /// Stores <paramref name="value"/> for <paramref name="fieldId"/> and re-projects every
    /// placement of the field's marker in Canvas and Timeline output. Called synchronously from the
    /// eval call stack when script assigns <c>input.value</c>; must not acquire
    /// <c>_evalSemaphore</c>. Never throws.
    /// </summary>
    void IFieldHost.SetFieldValue(Guid fieldId, FieldKind kind, string value)
    {
        try
        {
            var normalized = value ?? "";
            lock (this._stateLock)
            {
                this._fieldStore.SetValue(fieldId, normalized);
                this.UpdateFieldInCanvases(fieldId, kind, normalized);
                this.UpdateFieldInTimeline(fieldId, kind, normalized);

                // No explicit prune here: a value update only touches markers that are already
                // placed (UpdateFieldInCanvases already prunes via CommitCanvasMutation when it
                // actually commits a canvas). Pruning unconditionally would wipe out the value
                // this same call just seeded for a field that has not been placed anywhere yet
                // (e.g. the DisplayInput constructor's initial seed, before canvas.add runs).
            }
        }
        catch (InvalidOperationException ex)
        {
            this.AppendCanvasProjectionError(ex);
        }
        catch
        {
            // Swallow — a field update must never disrupt the eval.
        }
    }

    // IFilePickerHost — transactional attachment state (ADR-50)

    AttachmentPickerSnapshot IFilePickerHost.EnsureFilePicker(DisplayFilePicker picker) =>
        this._attachmentStore.EnsurePicker(picker);

    IReadOnlyList<DuetsPadFile> IFilePickerHost.GetFiles(Guid pickerId) =>
        this._attachmentStore.GetFiles(pickerId);

    Stream IFilePickerHost.OpenRead(Guid pickerId, Guid fileId) =>
        this._attachmentStore.OpenRead(pickerId, fileId);

    void IFilePickerHost.RemoveFile(DisplayFilePicker picker, Guid fileId)
    {
        if (this._attachmentStore.RemoveFile(picker.Id, fileId))
        {
            this.ProjectFilePicker(picker);
        }
    }

    void IFilePickerHost.ClearFiles(DisplayFilePicker picker)
    {
        if (this._attachmentStore.ClearFiles(picker.Id))
        {
            this.ProjectFilePicker(picker);
        }
    }

    internal async Task<BeginAttachmentSelectionResult> BeginAttachmentSelectionAsync(
        Guid pickerId,
        IReadOnlyList<AttachmentFileManifest> manifest,
        AttachmentSelectionOrder? order = null
    )
    {
        this.Touch();
        if (!await this.TryEnterEvalSemaphoreAsync().ConfigureAwait(false))
        {
            return new BeginAttachmentSelectionResult(
                false,
                Guid.Empty,
                0,
                [],
                "Session has been disposed.",
                false
            );
        }

        try
        {
            if (!this.IsLiveFilePicker(pickerId))
            {
                return new BeginAttachmentSelectionResult(
                    false,
                    Guid.Empty,
                    0,
                    [],
                    "The file picker is no longer available.",
                    false
                );
            }

            order ??= new AttachmentSelectionOrder(
                this._directAttachmentClientId,
                Interlocked.Increment(ref this._directAttachmentSequence)
            );
            var result = this._attachmentStore.BeginSelection(pickerId, order, manifest);
            this.ProjectFilePicker(pickerId);
            return result;
        }
        finally
        {
            this._evalSemaphore.Release();
        }
    }

    internal async Task<AttachmentOperationResult> UploadAttachmentFileAsync(
        Guid pickerId,
        Guid token,
        Guid fileId,
        Stream input,
        CancellationToken cancellationToken
    )
    {
        this.Touch();
        if (Volatile.Read(ref this._disposed) == 1)
        {
            return new AttachmentOperationResult(false, true, 0, "Session has been disposed.");
        }

        AttachmentOperationResult result;
        try
        {
            result = await this
                ._attachmentStore.UploadFileAsync(pickerId, token, fileId, input, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref this._disposed) == 1)
        {
            return new AttachmentOperationResult(false, true, 0, "Session has been disposed.");
        }
        if (
            !result.Ok
            && !result.Stale
            && await this.TryEnterEvalSemaphoreAsync().ConfigureAwait(false)
        )
        {
            try
            {
                this.ProjectFilePicker(pickerId);
            }
            finally
            {
                this._evalSemaphore.Release();
            }
        }

        return result;
    }

    internal Task<AttachmentOperationResult> CommitAttachmentSelectionAsync(
        Guid pickerId,
        Guid token
    ) =>
        this.MutateAttachmentSelectionAsync(
            pickerId,
            () => this._attachmentStore.CommitSelection(pickerId, token)
        );

    internal Task<AttachmentOperationResult> CancelAttachmentSelectionAsync(
        Guid pickerId,
        Guid token
    ) =>
        this.MutateAttachmentSelectionAsync(
            pickerId,
            () => this._attachmentStore.CancelSelection(pickerId, token)
        );

    internal Task<AttachmentOperationResult> CancelFailedAttachmentSelectionAsync(
        Guid pickerId,
        long expectedRevision
    ) =>
        this.MutateAttachmentSelectionAsync(
            pickerId,
            () => this._attachmentStore.CancelFailedSelection(pickerId, expectedRevision)
        );

    private async Task<AttachmentOperationResult> MutateAttachmentSelectionAsync(
        Guid pickerId,
        Func<AttachmentOperationResult> mutation
    )
    {
        this.Touch();
        if (!await this.TryEnterEvalSemaphoreAsync().ConfigureAwait(false))
        {
            return new AttachmentOperationResult(false, true, 0, "Session has been disposed.");
        }

        try
        {
            var result = mutation();
            if (!result.Stale)
            {
                this.ProjectFilePicker(pickerId);
            }

            return result;
        }
        finally
        {
            this._evalSemaphore.Release();
        }
    }

    private async Task<bool> TryEnterEvalSemaphoreAsync()
    {
        if (Volatile.Read(ref this._disposed) == 1)
        {
            return false;
        }

        try
        {
            await this._evalSemaphore.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        if (Volatile.Read(ref this._disposed) == 0)
        {
            return true;
        }

        this._evalSemaphore.Release();
        return false;
    }

    private bool IsLiveFilePicker(Guid pickerId)
    {
        lock (this._stateLock)
        {
            foreach (var projection in this._canvasProjections.Values)
            {
                var (markers, kind) = FieldMarker.FindWithKind(projection.State.Root, pickerId);
                if (markers.Count > 0 && kind == FieldKind.File)
                {
                    return true;
                }
            }

            foreach (var entry in this._timelineState)
            {
                var (markers, kind) = FieldMarker.FindWithKind(entry.Body, pickerId);
                if (markers.Count > 0 && kind == FieldKind.File)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void ProjectFilePicker(Guid pickerId)
    {
        if (this._attachmentStore.TryGetHandle(pickerId) is { } picker)
        {
            this.ProjectFilePicker(picker);
        }
    }

    private void ProjectFilePicker(DisplayFilePicker picker)
    {
        // Render enters the attachment-store lock. Complete it before taking _stateLock so this
        // projection path never introduces the reverse of the documented lock order.
        var content = picker.Render();
        lock (this._stateLock)
        {
            foreach (var name in this._canvasProjections.Keys.ToList())
            {
                var projection = this._canvasProjections[name];
                var markers = FieldMarker.Find(projection.State.Root, picker.Id);
                if (markers.Count == 0)
                {
                    continue;
                }

                var newRoot = (Element)
                    FieldMarker.Replace(projection.State.Root, markers, content.Body);
                var newState = new CanvasState(newRoot);
                if (newState.Equals(projection.State))
                {
                    continue;
                }

                var interactions = new CanvasInteractionCommitPlan(
                    name,
                    this._interactionStore.GetCanvasInteractions(name),
                    replacedInteractions: [],
                    replaceExisting: false
                );
                this.CommitCanvasMutation(
                    name,
                    existed: true,
                    projection,
                    newState,
                    projection.Revision + 1,
                    interactions
                );
            }

            for (var i = 0; i < this._timelineState.Count; i++)
            {
                var entry = this._timelineState[i];
                var markers = FieldMarker.Find(entry.Body, picker.Id);
                if (markers.Count == 0)
                {
                    continue;
                }

                var newBody = FieldMarker.Replace(entry.Body, markers, content.Body);
                if (newBody.Equals(entry.Body))
                {
                    continue;
                }

                var newEntry = new TimelineEntry(entry.Id, entry.Reason, newBody, entry.Timestamp);
                this._timelineState = this._timelineState.Replace(newEntry);
                var interactions = this._interactionStore.TimelineInteractions.TryGetValue(
                    entry.Id,
                    out var existing
                )
                    ? existing
                    : [];
                this.BroadcastTimeline(TimelineEventMessage.Update(newEntry, interactions));
            }
        }
    }

    /// <summary>
    /// Applies a browser-originated blur commit for <paramref name="fieldId"/> (ADR-47). Acquires
    /// <c>_evalSemaphore</c> before mutating state — the same discipline <see cref="InvokeInteractionAsync"/>
    /// uses for its field-snapshot application — so a blur commit arriving from the HTTP field-commit
    /// endpoint (which runs outside the eval call stack) cannot land between an in-progress eval's
    /// out-of-lock render step and its in-lock projection commit and desynchronize the projection.
    /// Delegates to <see cref="ApplyBrowserFieldCommit"/>, which discards the commit rather than
    /// reviving the store entry when the field's marker is no longer reachable from any canvas or
    /// Timeline content. Never throws.
    /// </summary>
    internal async Task CommitFieldValue(Guid fieldId, string value)
    {
        this.Touch();

        if (Volatile.Read(ref this._disposed) == 1)
        {
            return;
        }

        try
        {
            await this._evalSemaphore.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (Volatile.Read(ref this._disposed) == 1)
            {
                return;
            }

            var normalized = value ?? "";
            lock (this._stateLock)
            {
                this.ApplyBrowserFieldCommit(fieldId, normalized);
            }
        }
        catch
        {
            // Swallow — a browser commit must never surface as a hard failure.
        }
        finally
        {
            this._evalSemaphore.Release();
        }
    }

    /// <summary>
    /// Applies a browser-originated commit of <paramref name="value"/> for <paramref name="fieldId"/>:
    /// a no-op when the field's marker is no longer reachable from any canvas projection or Timeline
    /// entry (a stale, delayed commit from a since-removed control must not revive a value whose
    /// rendered content no longer exists, ADR-47), otherwise stores the value and updates the
    /// authoritative Canvas/Timeline state in place without broadcasting (no echo — the committing
    /// browser's own DOM already reflects the value). Shared by <see cref="CommitFieldValue"/> (the
    /// blur-commit HTTP path) and <see cref="InvokeInteractionAsync"/> (the invoke-body snapshot path)
    /// so both browser-originated commit routes apply the same liveness guard and projection update.
    /// Must be called while <c>_stateLock</c> is held.
    /// </summary>
    private void ApplyBrowserFieldCommit(Guid fieldId, string value)
    {
        if (!this.TryGetReachableFieldKind(fieldId, out var kind) || kind == FieldKind.File)
        {
            return;
        }

        this._fieldStore.SetValue(fieldId, value);
        this.CommitFieldValueInCanvases(fieldId, value);
        this.CommitFieldValueInTimeline(fieldId, value);
    }

    /// <summary>
    /// Returns whether the field identified by <paramref name="fieldId"/> currently has at least one
    /// marker placement in any canvas projection or Timeline entry. Must be called while
    /// <c>_stateLock</c> is held.
    /// </summary>
    private bool TryGetReachableFieldKind(Guid fieldId, out FieldKind kind)
    {
        foreach (var projection in this._canvasProjections.Values)
        {
            var (markers, candidate) = FieldMarker.FindWithKind(projection.State.Root, fieldId);
            if (markers.Count > 0 && candidate is { } resolved)
            {
                kind = resolved;
                return true;
            }
        }

        foreach (var entry in this._timelineState)
        {
            var (markers, candidate) = FieldMarker.FindWithKind(entry.Body, fieldId);
            if (markers.Count > 0 && candidate is { } resolved)
            {
                kind = resolved;
                return true;
            }
        }

        kind = default;
        return false;
    }

    /// <summary>
    /// Updates the value-encoding attribute of every placement of <paramref name="fieldId"/>'s
    /// marker in every canvas and broadcasts the resulting mutation. Must be called while
    /// <c>_stateLock</c> is held.
    /// </summary>
    private void UpdateFieldInCanvases(Guid fieldId, FieldKind kind, string value)
    {
        foreach (var name in this._canvasProjections.Keys.ToList())
        {
            var projection = this._canvasProjections[name];
            var markers = FieldMarker.Find(projection.State.Root, fieldId);
            if (markers.Count == 0)
            {
                continue;
            }

            var newRoot = (Element)
                FieldMarker.ApplyValue(projection.State.Root, markers, kind, value);
            var newState = new CanvasState(newRoot);
            if (newState.Equals(projection.State))
            {
                // Identical value: a true no-op. Skip so reassigning the same value does not emit
                // a phantom empty-operation patch or advance the revision.
                continue;
            }

            // A field carries no interactions of its own; the existing canvas interaction set is
            // preserved unchanged (paths still resolve because only attributes changed).
            var plan = new CanvasInteractionCommitPlan(
                name,
                this._interactionStore.GetCanvasInteractions(name),
                replacedInteractions: [],
                replaceExisting: false
            );
            this.CommitCanvasMutation(
                name,
                existed: true,
                projection,
                newState,
                projection.Revision + 1,
                plan
            );
        }
    }

    /// <summary>
    /// Updates the value-encoding attribute of every placement of <paramref name="fieldId"/>'s
    /// marker in every Timeline entry and broadcasts a <c>timeline.update</c> for each. Must be
    /// called while <c>_stateLock</c> is held.
    /// </summary>
    private void UpdateFieldInTimeline(Guid fieldId, FieldKind kind, string value)
    {
        for (var i = 0; i < this._timelineState.Count; i++)
        {
            var entry = this._timelineState[i];
            var markers = FieldMarker.Find(entry.Body, fieldId);
            if (markers.Count == 0)
            {
                continue;
            }

            var newBody = FieldMarker.ApplyValue(entry.Body, markers, kind, value);
            if (newBody.Equals(entry.Body))
            {
                // Identical value: skip the redundant timeline.update.
                continue;
            }

            var newEntry = new TimelineEntry(entry.Id, entry.Reason, newBody, entry.Timestamp);
            this._timelineState = this._timelineState.Replace(newEntry);
            var interactions = this._interactionStore.TimelineInteractions.TryGetValue(
                entry.Id,
                out var existing
            )
                ? existing
                : [];
            this.BroadcastTimeline(TimelineEventMessage.Update(newEntry, interactions));
        }
    }

    /// <summary>
    /// Updates the value-encoding attribute of every placement of <paramref name="fieldId"/>'s
    /// marker in every canvas projection's <see cref="CanvasState"/>, resolving each marker's
    /// <see cref="FieldKind"/> from its own <c>data-duetspad-field-kind</c> attribute (a
    /// browser-originated commit does not carry the kind). Unlike <see cref="UpdateFieldInCanvases"/>,
    /// this replaces only the projection's <c>State</c> at its current revision — it does not
    /// broadcast a patch and does not advance the revision (no echo). Must be called while
    /// <c>_stateLock</c> is held.
    /// </summary>
    private void CommitFieldValueInCanvases(Guid fieldId, string value)
    {
        foreach (var name in this._canvasProjections.Keys.ToList())
        {
            var projection = this._canvasProjections[name];
            var (markers, kind) = FieldMarker.FindWithKind(projection.State.Root, fieldId);
            if (markers.Count == 0 || kind is null)
            {
                continue;
            }

            var newRoot = (Element)
                FieldMarker.ApplyValue(projection.State.Root, markers, kind.Value, value);
            var newState = new CanvasState(newRoot);
            if (newState.Equals(projection.State))
            {
                // Identical value: a true no-op. Skip so re-committing the same value does not
                // needlessly rebuild the projection's State.
                continue;
            }

            this._canvasProjections[name] = projection with { State = newState };
        }
    }

    /// <summary>
    /// Updates the value-encoding attribute of every placement of <paramref name="fieldId"/>'s
    /// marker in every Timeline entry's body, resolving each marker's <see cref="FieldKind"/> from
    /// its own <c>data-duetspad-field-kind</c> attribute. Unlike <see cref="UpdateFieldInTimeline"/>,
    /// this does not broadcast a <c>timeline.update</c> (no echo). Must be called while
    /// <c>_stateLock</c> is held.
    /// </summary>
    private void CommitFieldValueInTimeline(Guid fieldId, string value)
    {
        for (var i = 0; i < this._timelineState.Count; i++)
        {
            var entry = this._timelineState[i];
            var (markers, kind) = FieldMarker.FindWithKind(entry.Body, fieldId);
            if (markers.Count == 0 || kind is null)
            {
                continue;
            }

            var newBody = FieldMarker.ApplyValue(entry.Body, markers, kind.Value, value);
            if (newBody.Equals(entry.Body))
            {
                // Identical value: skip the redundant entry replacement.
                continue;
            }

            var newEntry = new TimelineEntry(entry.Id, entry.Reason, newBody, entry.Timestamp);
            this._timelineState = this._timelineState.Replace(newEntry);
        }
    }

    /// <summary>
    /// Removes field-store entries whose marker is no longer reachable from any canvas projection
    /// or Timeline entry (ADR-47: a field's value shares the lifetime of its rendered content). Must
    /// be called while <c>_stateLock</c> is held.
    /// </summary>
    private void PruneFieldBackedState()
    {
        // Nothing to prune — skip the full-tree marker scan (every canvas plus every Timeline
        // entry) that would otherwise run on each Timeline append even in sessions that never
        // create a form input.
        if (this._fieldStore.IsEmpty && this._attachmentStore.IsEmpty)
        {
            return;
        }

        var retained = new HashSet<Guid>();
        foreach (var projection in this._canvasProjections.Values)
        {
            FieldMarker.CollectIds(projection.State.Root, retained);
        }

        foreach (var entry in this._timelineState)
        {
            FieldMarker.CollectIds(entry.Body, retained);
        }

        this._fieldStore.Retain(retained);
        this._attachmentStore.Retain(retained);
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

    /// <summary>
    /// Invokes the interaction handler registered under <paramref name="handlerId"/>, optionally
    /// first applying every entry of <paramref name="fieldSnapshot"/> as a browser-originated field
    /// commit via <see cref="ApplyBrowserFieldCommit"/> (ADR-47: the invoke body carries a snapshot of
    /// edited-but-not-yet-blurred field values so the handler observes the latest edit regardless of
    /// blur timing, and so the Canvas/Timeline projection — not only the field store — reflects the
    /// snapshot, exactly as a blur commit does). Before applying fields or calling the handler,
    /// validates <paramref name="attachmentRevisions"/> and rejects any unsettled or changed picker
    /// state (ADR-50). Unreachable field-backed state is pruned before validation so a picker that
    /// was rendered speculatively but never committed cannot poison the browser snapshot. Pruning,
    /// validation, snapshot application, and handler invocation all run under
    /// <c>_evalSemaphore</c>.
    /// </summary>
    internal async Task<InteractionInvokeResult> InvokeInteractionAsync(
        Guid handlerId,
        IReadOnlyDictionary<Guid, string>? fieldSnapshot = null,
        IReadOnlyDictionary<Guid, long>? attachmentRevisions = null
    )
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

            lock (this._stateLock)
            {
                this.PruneFieldBackedState();
            }

            var attachmentValidation = this._attachmentStore.ValidateInvoke(attachmentRevisions);
            if (!attachmentValidation.Ok)
            {
                return InteractionInvokeResult.AttachmentStateChanged(
                    attachmentValidation.Error ?? "Attachment state changed before invocation."
                );
            }

            if (fieldSnapshot is { Count: > 0 })
            {
                lock (this._stateLock)
                {
                    foreach (var (snapshotFieldId, snapshotValue) in fieldSnapshot)
                    {
                        this.ApplyBrowserFieldCommit(snapshotFieldId, snapshotValue ?? "");
                    }
                }
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

    internal bool TryGetCanvasSnapshot(string name, out CanvasEventMessage snapshot)
    {
        this.Touch();

        lock (this._stateLock)
        {
            if (this._canvasProjections.TryGetValue(name, out var projection))
            {
                snapshot = CanvasEventMessage.Snapshot(
                    name,
                    projection.State,
                    this._interactionStore.GetCanvasInteractions(name),
                    projection.Revision
                );
                return true;
            }
        }

        snapshot = null!;
        return false;
    }

    internal async Task<TaggedTemplateCompletionDispatchResult> CompleteTaggedTemplateAsync(
        TemplateCompletionContext context,
        CancellationToken cancellationToken = default
    )
    {
        this.Touch();

        if (Volatile.Read(ref this._disposed) == 1)
        {
            return TaggedTemplateCompletionDispatchResult.Failed("Session has been disposed.");
        }

        if (!this.DuetsSession.TaggedTemplates.TryGet(context.Tag, out var registration))
        {
            return TaggedTemplateCompletionDispatchResult.Empty();
        }

        if (!this.TryEnterCompletionRateLimit())
        {
            return TaggedTemplateCompletionDispatchResult.Failed(
                "Tagged-template completion rate limit exceeded."
            );
        }

        var requestCancellation = new CancellationTokenSource();
        this.ReplaceActiveCompletionCancellation(requestCancellation);

        try
        {
            await this._completionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            this.ClearActiveCompletionCancellation(requestCancellation);
            requestCancellation.Dispose();
            return TaggedTemplateCompletionDispatchResult.Superseded();
        }

        var releaseInFinally = true;
        var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellation.Token,
            cancellationToken
        );
        var delayCancellation = new CancellationTokenSource();
        try
        {
            if (requestCancellation.IsCancellationRequested)
            {
                return TaggedTemplateCompletionDispatchResult.Superseded();
            }

            var completionTask = registration.Complete(context, timeoutCancellation.Token).AsTask();
            var delayTask = Task.Delay(
                this._taggedTemplateCompletionTimeout,
                delayCancellation.Token
            );
            var completed = await Task.WhenAny(completionTask, delayTask).ConfigureAwait(false);

            if (completed == delayTask)
            {
                requestCancellation.Cancel();
                timeoutCancellation.Cancel();
                releaseInFinally = false;
                _ = completionTask.ContinueWith(
                    task =>
                    {
                        _ = task.Exception;
                        this.ClearActiveCompletionCancellation(requestCancellation);
                        timeoutCancellation.Dispose();
                        delayCancellation.Dispose();
                        requestCancellation.Dispose();
                        this._completionSemaphore.Release();
                    },
                    TaskScheduler.Default
                );
                return TaggedTemplateCompletionDispatchResult.Timeout();
            }

            delayCancellation.Cancel();
            var items = await completionTask.ConfigureAwait(false);
            if (requestCancellation.IsCancellationRequested)
            {
                return TaggedTemplateCompletionDispatchResult.Superseded();
            }

            var valid = items
                .Where(item =>
                    TaggedTemplateRegistry.Validate(item)
                    && (
                        item.ReplacementSpan is null
                        || TaggedTemplateRegistry.IsSpanWithinSegment(
                            item.ReplacementSpan.Value,
                            context.CurrentSegmentRaw.Length
                        )
                    )
                )
                .ToList();
            return TaggedTemplateCompletionDispatchResult.Success(
                TaggedTemplateRegistry.Cap(valid)
            );
        }
        catch (OperationCanceledException)
        {
            return TaggedTemplateCompletionDispatchResult.Superseded();
        }
        catch (Exception ex)
        {
            return TaggedTemplateCompletionDispatchResult.Failed(ex.Message);
        }
        finally
        {
            if (releaseInFinally)
            {
                this.ClearActiveCompletionCancellation(requestCancellation);
                timeoutCancellation.Dispose();
                delayCancellation.Dispose();
                requestCancellation.Dispose();
                this._completionSemaphore.Release();
            }
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

    void IToastHost.ShowToast(string message, ToastOptions options)
    {
        this.EnqueueControl(
            ControlEventTypes.Toast,
            new Dictionary<string, object?>
            {
                ["message"] = message,
                ["title"] = options.Title,
                ["variant"] = options.Variant,
                ["durationMs"] = options.DurationMilliseconds,
            }
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
        var completionSemaphoreHeld = false;
        try
        {
            this.DuetsSession.ConsoleLogged -= this.OnConsoleLogged;
            this.DuetsSession.Declarations.DeclarationChanged -= this.OnDeclarationChanged;
            if (this._taggedTemplateSnapshotsEnabled)
            {
                this.DuetsSession.TaggedTemplates.Changed -= this.OnTaggedTemplateRegistryChanged;
            }

            this.CancelActiveCompletion();

            this._completionSemaphore.Wait();
            completionSemaphoreHeld = true;

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

                this._canvasProjections.Clear();
                this._interactionStore.Clear();
                this._fieldStore.Clear();
            }

            Exception? attachmentDisposeError = null;
            try
            {
                this._attachmentStore.Dispose();
            }
            catch (Exception ex)
            {
                attachmentDisposeError = ex;
            }

            try
            {
                this.DuetsSession.Dispose();
            }
            catch (Exception ex) when (attachmentDisposeError is not null)
            {
                throw new AggregateException(attachmentDisposeError, ex);
            }

            if (attachmentDisposeError is not null)
            {
                throw attachmentDisposeError;
            }
        }
        finally
        {
            if (completionSemaphoreHeld)
            {
                this._completionSemaphore.Release();
            }

            this._evalSemaphore.Release();
            this._evalSemaphore.Dispose();
            this._completionSemaphore.Dispose();
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

    internal void OnTaggedTemplateRegistryChanged(TaggedTemplateRegistrySnapshot snapshot)
    {
        if (!this._taggedTemplateSnapshotsEnabled)
        {
            return;
        }

        try
        {
            lock (this._stateLock)
            {
                this.BroadcastTaggedTemplateSnapshot(snapshot);
            }
        }
        catch
        {
            // Swallow — must not disrupt registration.
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

            this.PruneFieldBackedState();
        }
    }

    private void AppendCanvasProjectionError(Exception ex)
    {
        try
        {
            this.AppendTimelineEntry(
                "render-error",
                DisplayContent.Text($"Canvas projection rejected: {ex.Message}")
            );
        }
        catch
        {
            // Swallow.
        }
    }

    /// <summary>
    /// Commits a Canvas state/interaction mutation, chooses the smallest supported event shape,
    /// and broadcasts it to all registered subscribers. Must be called while <c>_stateLock</c>
    /// is held.
    /// </summary>
    private void CommitCanvasMutation(
        string name,
        bool existed,
        CanvasProjection oldProjection,
        CanvasState newState,
        long revision,
        CanvasInteractionCommitPlan interactions
    )
    {
        ValidateCanvasInteractions(newState, interactions.Interactions);

        var message = this.CreateCanvasMutationMessage(
            name,
            existed,
            oldProjection,
            newState,
            revision,
            interactions.Interactions
        );

        this._interactionStore.CommitCanvasInteractions(interactions);
        this._canvasProjections[name] = new CanvasProjection(newState, revision);
        this.BroadcastCanvas(message);
        this.PruneFieldBackedState();
    }

    private CanvasEventMessage CreateCanvasMutationMessage(
        string name,
        bool existed,
        CanvasProjection oldProjection,
        CanvasState newState,
        long revision,
        IReadOnlyList<CommittedInteraction> interactions
    )
    {
        var replace = CanvasEventMessage.Replace(name, newState, interactions, revision);
        if (!existed)
        {
            return replace;
        }

        var operations = this._canvasDiffer.Diff(oldProjection.State, newState);
        var patch = CanvasEventMessage.Patch(
            name,
            oldProjection.Revision,
            revision,
            operations,
            interactions
        );

        return SerializedByteLength(patch) < SerializedByteLength(replace) ? patch : replace;
    }

    /// <summary>
    /// Enqueues a Canvas event to all registered subscribers. Must be called while
    /// <c>_stateLock</c> is held.
    /// </summary>
    private void BroadcastCanvas(CanvasEventMessage msg)
    {
        var padMsg = new PadEventMessage.Canvas(msg);
        foreach (var (_, writer) in this._eventSubscribers)
        {
            writer.TryWrite(padMsg);
        }
    }

    private static int SerializedByteLength(CanvasEventMessage message) =>
        Encoding.UTF8.GetByteCount(SseSerializer.Serialize(message));

    private static bool CanvasInteractionsEqual(
        IReadOnlyList<CommittedInteraction> oldInteractions,
        IReadOnlyList<CommittedInteraction> newInteractions
    )
    {
        if (oldInteractions.Count != newInteractions.Count)
        {
            return false;
        }

        var oldByKey = oldInteractions.ToDictionary(CanvasInteractionKey, StringComparer.Ordinal);
        foreach (var newInteraction in newInteractions)
        {
            if (!oldByKey.TryGetValue(CanvasInteractionKey(newInteraction), out var oldInteraction))
            {
                return false;
            }

            if (
                oldInteraction.State != InteractionState.Live
                || newInteraction.State != InteractionState.Live
                || !ReferenceEquals(oldInteraction.Handler, newInteraction.Handler)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanvasInteractionsEqual(
        IReadOnlyList<CommittedInteraction> oldInteractions,
        PendingInteractions newInteractions,
        int? childIndex
    )
    {
        if (oldInteractions.Count != newInteractions.Count)
        {
            return false;
        }

        var oldByKey = oldInteractions.ToDictionary(CanvasInteractionKey, StringComparer.Ordinal);
        foreach (var interaction in newInteractions)
        {
            var target = childIndex is int index
                ? interaction.Target.Prepend(index)
                : interaction.Target;
            if (
                !oldByKey.TryGetValue(
                    CanvasInteractionKey(target, interaction.Event),
                    out var oldInteraction
                )
            )
            {
                return false;
            }

            if (
                oldInteraction.State != InteractionState.Live
                || !ReferenceEquals(oldInteraction.Handler, interaction.Handler)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateCanvasInteractions(
        CanvasState state,
        IReadOnlyList<CommittedInteraction> interactions
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var interaction in interactions)
        {
            if (interaction.State != InteractionState.Live)
            {
                throw new InvalidOperationException("Canvas interactions must be live.");
            }

            if (interaction.Event != InteractionEvent.Click)
            {
                throw new InvalidOperationException("Canvas interaction event is invalid.");
            }

            if (!CanvasInteractionTargetResolves(state, interaction.Target))
            {
                throw new InvalidOperationException("Canvas interaction target is invalid.");
            }

            if (!seen.Add(CanvasInteractionKey(interaction)))
            {
                throw new InvalidOperationException(
                    "Canvas interactions must be unique by target and event."
                );
            }
        }
    }

    private static void ValidateCanvasInteractions(
        CanvasState state,
        IReadOnlyList<CommittedInteraction> existingInteractions,
        PendingInteractions pendingInteractions,
        int? childIndex
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var interaction in existingInteractions)
        {
            if (interaction.State != InteractionState.Live)
            {
                throw new InvalidOperationException("Canvas interactions must be live.");
            }

            if (interaction.Event != InteractionEvent.Click)
            {
                throw new InvalidOperationException("Canvas interaction event is invalid.");
            }

            if (!CanvasInteractionTargetResolves(state, interaction.Target))
            {
                throw new InvalidOperationException("Canvas interaction target is invalid.");
            }

            if (!seen.Add(CanvasInteractionKey(interaction)))
            {
                throw new InvalidOperationException(
                    "Canvas interactions must be unique by target and event."
                );
            }
        }

        foreach (var interaction in pendingInteractions)
        {
            if (interaction.Event != InteractionEvent.Click)
            {
                throw new InvalidOperationException("Canvas interaction event is invalid.");
            }

            var target = childIndex is int index
                ? interaction.Target.Prepend(index)
                : interaction.Target;
            if (!CanvasInteractionTargetResolves(state, target))
            {
                throw new InvalidOperationException("Canvas interaction target is invalid.");
            }

            if (!seen.Add(CanvasInteractionKey(target, interaction.Event)))
            {
                throw new InvalidOperationException(
                    "Canvas interactions must be unique by target and event."
                );
            }
        }
    }

    private static bool CanvasInteractionTargetResolves(CanvasState state, DisplayPath target)
    {
        ITerminalRenderNode node = state.Root;
        foreach (var segment in target.Segments)
        {
            if (node is not Element element || segment >= element.Children.Count)
            {
                return false;
            }

            node = element.Children[segment];
        }

        return node is Element;
    }

    private static string CanvasInteractionKey(CommittedInteraction interaction) =>
        $"{PathKey(interaction.Target)}|{interaction.Event}";

    private static string CanvasInteractionKey(DisplayPath target, InteractionEvent @event) =>
        $"{PathKey(target)}|{@event}";

    private static string PathKey(DisplayPath path) => string.Join("/", path.Segments);

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

    private void BroadcastTaggedTemplateSnapshot(TaggedTemplateRegistrySnapshot snapshot)
    {
        var padMsg = new PadEventMessage.TaggedTemplateSnapshot(snapshot);
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
            foreach (var (canvasName, projection) in this._canvasProjections)
            {
                writer.TryWrite(
                    new PadEventMessage.Canvas(
                        CanvasEventMessage.Snapshot(
                            canvasName,
                            projection.State,
                            this._interactionStore.GetCanvasInteractions(canvasName),
                            projection.Revision
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

            if (this._taggedTemplateSnapshotsEnabled)
            {
                writer.TryWrite(
                    new PadEventMessage.TaggedTemplateSnapshot(
                        this.DuetsSession.TaggedTemplates.GetSnapshot()
                    )
                );
            }
        }

        return key;
    }

    /// <summary>
    /// Removes the unified event subscriber identified by <paramref name="key"/>.
    /// </summary>
    internal void UnsubscribeEvents(Guid key) => this._eventSubscribers.TryRemove(key, out _);

    private bool TryEnterCompletionRateLimit()
    {
        var now = this._clock();
        var windowStart = now - TimeSpan.FromSeconds(1);
        lock (this._completionLock)
        {
            while (
                this._completionRequestTimes.Count > 0
                && this._completionRequestTimes.Peek() < windowStart
            )
            {
                this._completionRequestTimes.Dequeue();
            }

            if (
                this._completionRequestTimes.Count
                >= this._taggedTemplateCompletionRateLimitPerSecond
            )
            {
                return false;
            }

            this._completionRequestTimes.Enqueue(now);
            return true;
        }
    }

    private void ReplaceActiveCompletionCancellation(CancellationTokenSource requestCancellation)
    {
        CancellationTokenSource? previous;
        lock (this._completionLock)
        {
            previous = this._activeCompletionCancellation;
            this._activeCompletionCancellation = requestCancellation;
        }

        CancelIgnoringDisposed(previous);
    }

    private void ClearActiveCompletionCancellation(CancellationTokenSource requestCancellation)
    {
        lock (this._completionLock)
        {
            if (ReferenceEquals(this._activeCompletionCancellation, requestCancellation))
            {
                this._activeCompletionCancellation = null;
            }
        }
    }

    private void CancelActiveCompletion()
    {
        CancellationTokenSource? active;
        lock (this._completionLock)
        {
            active = this._activeCompletionCancellation;
            this._activeCompletionCancellation = null;
        }

        CancelIgnoringDisposed(active);
    }

    private static void CancelIgnoringDisposed(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Cancellation and disposal race during timeout cleanup; disposal already wins.
        }
    }
}
