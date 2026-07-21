using Duets.Pad.Rendering;

namespace Duets.Pad.Interactions;

/// <summary>
/// Owns the interaction lifecycle within a single DuetsPad session: committing pending
/// interactions, keyed storage by Timeline entry id, canvas, or modal, and handler lookup/release.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread safety:</b> this type has no internal locking. The caller
/// (<see cref="DuetsPadSession"/>) is responsible for holding <c>_stateLock</c> for every
/// mutating call and for every lookup that must be atomic with respect to state changes.
/// </para>
/// </remarks>
internal sealed class InteractionStore
{
    private readonly InteractionRegistry _registry = new();

    // Committed interactions keyed by Timeline entry id.
    private readonly Dictionary<long, IReadOnlyList<CommittedInteraction>> _timelineInteractions =
    [];

    // Committed interactions keyed by canvas name.
    private readonly Dictionary<string, IReadOnlyList<CommittedInteraction>> _canvasInteractions =
        new(StringComparer.Ordinal);

    // Committed interactions keyed by modal id.
    private readonly Dictionary<Guid, IReadOnlyList<CommittedInteraction>> _modalInteractions = [];

    /// <summary>
    /// Returns the committed interactions for the canvas with the given <paramref name="name"/>,
    /// or an empty list if no interactions have been committed for that canvas.
    /// </summary>
    public IReadOnlyList<CommittedInteraction> GetCanvasInteractions(string name) =>
        this._canvasInteractions.TryGetValue(name, out var interactions) ? interactions : [];

    /// <summary>
    /// Returns the committed interactions for <paramref name="modalId"/>.
    /// </summary>
    public IReadOnlyList<CommittedInteraction> GetModalInteractions(Guid modalId) =>
        this._modalInteractions.TryGetValue(modalId, out var interactions) ? interactions : [];

    /// <summary>
    /// Prepares a complete interaction set for a modal without publishing its handlers.
    /// </summary>
    public ModalInteractionCommitPlan PrepareSetModalInteractions(
        Guid modalId,
        PendingInteractions pending,
        int? childIndex = null
    )
    {
        var prepared = this.Prepare(pending, childIndex);
        return new ModalInteractionCommitPlan(
            modalId,
            prepared.Interactions,
            this.GetModalInteractions(modalId),
            prepared.Registrations
        );
    }

    /// <summary>
    /// Prepares replacement of interactions nested below modal slot markers.
    /// </summary>
    public ModalInteractionCommitPlan PrepareReplaceModalSlots(
        Guid modalId,
        IReadOnlyList<SlotInteractionReplacement> replacements
    )
    {
        var (kept, replaced, committed, registrations) = this.PlanSlotReplacements(
            this.GetModalInteractions(modalId),
            replacements
        );
        return new ModalInteractionCommitPlan(
            modalId,
            [.. kept, .. committed],
            replaced,
            registrations
        );
    }

    /// <summary>
    /// Publishes a prepared modal interaction set and releases the replaced handlers.
    /// </summary>
    public IReadOnlyList<CommittedInteraction> CommitModalInteractions(
        ModalInteractionCommitPlan plan
    )
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        foreach (var registration in plan.Registrations)
        {
            this._registry.Commit(registration);
        }

        if (plan.Interactions.Count > 0)
        {
            this._modalInteractions[plan.ModalId] = plan.Interactions;
        }
        else
        {
            this._modalInteractions.Remove(plan.ModalId);
        }

        this.Release(plan.ReplacedInteractions);
        return plan.Interactions;
    }

    /// <summary>
    /// Removes and unregisters every interaction owned by <paramref name="modalId"/>.
    /// </summary>
    public void ClearModalInteractions(Guid modalId)
    {
        if (this._modalInteractions.Remove(modalId, out var interactions))
        {
            this.Release(interactions);
        }
    }

    /// <summary>
    /// Commits <paramref name="pending"/> interactions as the new canvas interaction set for the
    /// canvas named <paramref name="name"/>, replacing (and unregistering) any previous interactions
    /// for that canvas.
    /// </summary>
    /// <param name="name">The canvas name.</param>
    /// <param name="pending">Pending interactions produced by the rendered content.</param>
    /// <param name="childIndex">
    /// When non-null, each interaction target is prepended with this segment index (used when
    /// appending a child to an existing canvas root rather than replacing it).
    /// </param>
    public IReadOnlyList<CommittedInteraction> SetCanvasInteractions(
        string name,
        PendingInteractions pending,
        int? childIndex = null
    ) =>
        this.CommitCanvasInteractions(this.PrepareSetCanvasInteractions(name, pending, childIndex));

    /// <summary>
    /// Appends committed interactions for <paramref name="pending"/> to the existing canvas
    /// interaction set for the canvas named <paramref name="name"/> without releasing the old ones.
    /// </summary>
    /// <param name="name">The canvas name.</param>
    /// <param name="pending">Pending interactions produced by the appended content.</param>
    /// <param name="childIndex">
    /// Segment index prepended to each target (the index of the newly appended child).
    /// </param>
    public IReadOnlyList<CommittedInteraction> AppendCanvasInteractions(
        string name,
        PendingInteractions pending,
        int childIndex
    )
    {
        return this.CommitCanvasInteractions(
            this.PrepareAppendCanvasInteractions(name, pending, childIndex)
        );
    }

    /// <summary>
    /// Prepares <paramref name="pending"/> interactions as the new canvas interaction set for
    /// the canvas named <paramref name="name"/> without publishing their handlers yet.
    /// </summary>
    public CanvasInteractionCommitPlan PrepareSetCanvasInteractions(
        string name,
        PendingInteractions pending,
        int? childIndex = null
    )
    {
        var existing = this.GetCanvasInteractions(name);
        var prepared = this.Prepare(pending, childIndex);
        return new CanvasInteractionCommitPlan(
            name,
            prepared.Interactions,
            existing,
            prepared.Registrations
        );
    }

    /// <summary>
    /// Prepares <paramref name="pending"/> interactions to be appended to the existing canvas
    /// interaction set for the canvas named <paramref name="name"/> without publishing their
    /// handlers yet.
    /// </summary>
    public CanvasInteractionCommitPlan PrepareAppendCanvasInteractions(
        string name,
        PendingInteractions pending,
        int childIndex
    )
    {
        var existing = this.GetCanvasInteractions(name);
        var prepared = this.Prepare(pending, childIndex);
        return new CanvasInteractionCommitPlan(
            name,
            [.. existing, .. prepared.Interactions],
            [],
            prepared.Registrations
        );
    }

    /// <summary>
    /// Clears the interactions for the canvas named <paramref name="name"/>, unregistering their
    /// handlers.
    /// </summary>
    public IReadOnlyList<CommittedInteraction> ClearCanvasInteractions(string name)
    {
        if (this._canvasInteractions.Remove(name, out var interactions))
        {
            this.Release(interactions);
        }

        return [];
    }

    /// <summary>
    /// Prepares clearing all interactions for the canvas named <paramref name="name"/>.
    /// </summary>
    public CanvasInteractionCommitPlan PrepareClearCanvasInteractions(string name) =>
        new(name, [], this.GetCanvasInteractions(name), replaceExisting: true);

    /// <summary>
    /// Prepares a slot-update commit for the canvas named <paramref name="name"/>: interactions
    /// under each replacement's marker path are dropped (released on commit), the new content's
    /// interactions are registered rebased under <c>markerPath + [0]</c>, and all other interactions
    /// are preserved.
    /// </summary>
    public CanvasInteractionCommitPlan PrepareReplaceCanvasSlots(
        string name,
        IReadOnlyList<SlotInteractionReplacement> replacements
    )
    {
        var (kept, replaced, committed, registrations) = this.PlanSlotReplacements(
            this.GetCanvasInteractions(name),
            replacements
        );
        return new CanvasInteractionCommitPlan(
            name,
            [.. kept, .. committed],
            replaced,
            registrations
        );
    }

    /// <summary>
    /// Applies a slot update to the Timeline entry with id <paramref name="entryId"/>: drops and
    /// releases interactions under each replacement's marker path, commits the new content's
    /// interactions rebased under <c>markerPath + [0]</c>, preserves all others, and returns the
    /// resulting interaction set for the entry.
    /// </summary>
    public IReadOnlyList<CommittedInteraction> ReplaceTimelineSlots(
        long entryId,
        IReadOnlyList<SlotInteractionReplacement> replacements
    )
    {
        var existing = this._timelineInteractions.TryGetValue(entryId, out var current)
            ? current
            : [];
        var (kept, replaced, committed, registrations) = this.PlanSlotReplacements(
            existing,
            replacements
        );

        foreach (var registration in registrations)
        {
            this._registry.Commit(registration);
        }

        this.Release(replaced);

        IReadOnlyList<CommittedInteraction> result = [.. kept, .. committed];
        if (result.Count > 0)
        {
            this._timelineInteractions[entryId] = result;
        }
        else
        {
            this._timelineInteractions.Remove(entryId);
        }

        return result;
    }

    private (
        List<CommittedInteraction> Kept,
        List<CommittedInteraction> Replaced,
        List<CommittedInteraction> Committed,
        List<PreparedInteractionRegistration> Registrations
    ) PlanSlotReplacements(
        IReadOnlyList<CommittedInteraction> existing,
        IReadOnlyList<SlotInteractionReplacement> replacements
    )
    {
        var kept = new List<CommittedInteraction>();
        var replaced = new List<CommittedInteraction>();
        foreach (var interaction in existing)
        {
            if (replacements.Any(r => interaction.Target.StartsWith(r.MarkerPath)))
            {
                replaced.Add(interaction);
            }
            else
            {
                kept.Add(interaction);
            }
        }

        var committed = new List<CommittedInteraction>();
        var registrations = new List<PreparedInteractionRegistration>();
        foreach (var replacement in replacements)
        {
            var prefix = new List<int>(replacement.MarkerPath.Segments) { 0 };
            var prepared = this.Prepare(replacement.Pending, prefix);
            committed.AddRange(prepared.Interactions);
            registrations.AddRange(prepared.Registrations);
        }

        return (kept, replaced, committed, registrations);
    }

    /// <summary>
    /// Publishes the prepared handlers in <paramref name="plan"/>, updates the committed canvas
    /// interaction set, and releases replaced handlers.
    /// </summary>
    public IReadOnlyList<CommittedInteraction> CommitCanvasInteractions(
        CanvasInteractionCommitPlan plan
    )
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        foreach (var registration in plan.Registrations)
        {
            this._registry.Commit(registration);
        }

        if (plan.Interactions.Count > 0)
        {
            this._canvasInteractions[plan.Name] = plan.Interactions;
        }
        else
        {
            this._canvasInteractions.Remove(plan.Name);
        }

        this.Release(plan.ReplacedInteractions);

        return plan.Interactions;
    }

    /// <summary>
    /// Returns the full map of committed Timeline interactions keyed by entry id.
    /// Used when broadcasting a timeline snapshot to newly connected subscribers.
    /// </summary>
    public IReadOnlyDictionary<long, IReadOnlyList<CommittedInteraction>> TimelineInteractions =>
        this._timelineInteractions;

    /// <summary>
    /// Commits <paramref name="pending"/> interactions and stores them under
    /// <paramref name="entryId"/> if any were committed.
    /// </summary>
    /// <returns>The committed interaction list (empty when there were no pending interactions).</returns>
    public IReadOnlyList<CommittedInteraction> CommitTimelineInteractions(
        long entryId,
        PendingInteractions pending
    )
    {
        var committed = this.Commit(pending, childIndex: null);
        if (committed.Count > 0)
        {
            this._timelineInteractions[entryId] = committed;
        }

        return committed;
    }

    /// <summary>
    /// Releases and removes the interactions associated with <paramref name="removedIds"/>.
    /// </summary>
    public void DiscardTimelineInteractions(IReadOnlyList<long> removedIds)
    {
        foreach (var id in removedIds)
        {
            if (this._timelineInteractions.Remove(id, out var interactions))
            {
                this.Release(interactions);
            }
        }
    }

    /// <summary>
    /// Attempts to find the handler registered under <paramref name="handlerId"/>.
    /// </summary>
    public bool TryGetHandler(Guid handlerId, out Action? handler) =>
        this._registry.TryGet(handlerId, out handler);

    /// <summary>
    /// Clears all interactions and unregisters all handlers.
    /// Called on session dispose under <c>_stateLock</c>.
    /// </summary>
    public void Clear()
    {
        this._registry.Clear();
        this._canvasInteractions.Clear();
        this._timelineInteractions.Clear();
        this._modalInteractions.Clear();
    }

    private IReadOnlyList<CommittedInteraction> Commit(
        PendingInteractions interactions,
        int? childIndex
    )
    {
        var prepared = this.Prepare(interactions, childIndex);
        foreach (var registration in prepared.Registrations)
        {
            this._registry.Commit(registration);
        }

        return prepared.Interactions;
    }

    private PreparedInteractions Prepare(PendingInteractions interactions, int? childIndex) =>
        this.Prepare(
            interactions,
            interaction =>
                childIndex is int index ? interaction.Target.Prepend(index) : interaction.Target
        );

    private PreparedInteractions Prepare(
        PendingInteractions interactions,
        IReadOnlyList<int> prefix
    ) => this.Prepare(interactions, interaction => interaction.Target.Prepend(prefix));

    private PreparedInteractions Prepare(
        PendingInteractions interactions,
        Func<PendingInteraction, DisplayPath> rebaseTarget
    )
    {
        if (interactions.Count == 0)
        {
            return PreparedInteractions.Empty;
        }

        var committed = new List<CommittedInteraction>(interactions.Count);
        var registrations = new List<PreparedInteractionRegistration>(interactions.Count);
        foreach (var interaction in interactions)
        {
            var target = rebaseTarget(interaction);
            var registration = this._registry.Prepare(interaction.Handler);
            registrations.Add(registration);
            committed.Add(
                new CommittedInteraction(
                    target,
                    interaction.Event,
                    registration.HandlerId,
                    InteractionState.Live,
                    interaction.Handler
                )
            );
        }

        return new PreparedInteractions(committed, registrations);
    }

    private void Release(IEnumerable<CommittedInteraction> interactions) =>
        this._registry.Unregister(interactions.Select(i => i.HandlerId));

    private sealed record PreparedInteractions(
        IReadOnlyList<CommittedInteraction> Interactions,
        IReadOnlyList<PreparedInteractionRegistration> Registrations
    )
    {
        public static PreparedInteractions Empty { get; } = new([], []);
    }
}

/// <summary>
/// Describes one slot replacement: the marker path in the surface tree and the pending
/// interactions produced by the slot's freshly rendered content (relative to the content root).
/// </summary>
internal sealed record SlotInteractionReplacement(
    DisplayPath MarkerPath,
    PendingInteractions Pending
);

internal sealed record CanvasInteractionCommitPlan
{
    public CanvasInteractionCommitPlan(
        string name,
        IReadOnlyList<CommittedInteraction> interactions,
        IReadOnlyList<CommittedInteraction> replacedInteractions,
        IReadOnlyList<PreparedInteractionRegistration> registrations
    )
    {
        this.Name = !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new ArgumentException("Canvas name cannot be empty.", nameof(name));
        this.Interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        this.ReplacedInteractions =
            replacedInteractions ?? throw new ArgumentNullException(nameof(replacedInteractions));
        this.Registrations =
            registrations ?? throw new ArgumentNullException(nameof(registrations));
    }

    public CanvasInteractionCommitPlan(
        string name,
        IReadOnlyList<CommittedInteraction> interactions,
        IReadOnlyList<CommittedInteraction> replacedInteractions,
        bool replaceExisting
    )
        : this(name, interactions, replaceExisting ? replacedInteractions : [], []) { }

    public string Name { get; }

    public IReadOnlyList<CommittedInteraction> Interactions { get; }

    public IReadOnlyList<CommittedInteraction> ReplacedInteractions { get; }

    public IReadOnlyList<PreparedInteractionRegistration> Registrations { get; }
}

internal sealed record ModalInteractionCommitPlan(
    Guid ModalId,
    IReadOnlyList<CommittedInteraction> Interactions,
    IReadOnlyList<CommittedInteraction> ReplacedInteractions,
    IReadOnlyList<PreparedInteractionRegistration> Registrations
);
