using Duets.Pad.Rendering;

namespace Duets.Pad.Interactions;

/// <summary>
/// Owns the interaction lifecycle within a single DuetsPad session: committing pending
/// interactions, keyed storage by Timeline entry id or canvas, and handler lookup/release.
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

    // Canvas interactions

    // Committed interactions currently displayed on the canvas.

    /// <summary>
    /// Returns the current canvas interactions.
    /// </summary>
    public IReadOnlyList<CommittedInteraction> CanvasInteractions { get; private set; } = [];

    /// <summary>
    /// Commits <paramref name="pending"/> interactions as the new canvas interaction set,
    /// replacing (and unregistering) any previous canvas interactions.
    /// </summary>
    /// <param name="pending">Pending interactions produced by the rendered content.</param>
    /// <param name="childIndex">
    /// When non-null, each interaction target is prepended with this segment index (used when
    /// appending a child to an existing canvas root rather than replacing it).
    /// </param>
    public void SetCanvasInteractions(PendingInteractions pending, int? childIndex = null)
    {
        this.Release(this.CanvasInteractions);
        this.CanvasInteractions = this.Commit(pending, childIndex);
    }

    /// <summary>
    /// Appends committed interactions for <paramref name="pending"/> to the existing canvas
    /// interaction set without releasing the old ones.
    /// </summary>
    /// <param name="pending">Pending interactions produced by the appended content.</param>
    /// <param name="childIndex">
    /// Segment index prepended to each target (the index of the newly appended child).
    /// </param>
    public void AppendCanvasInteractions(PendingInteractions pending, int childIndex)
    {
        var committed = this.Commit(pending, childIndex);
        if (committed.Count > 0)
        {
            this.CanvasInteractions = [.. this.CanvasInteractions, .. committed];
        }
    }

    /// <summary>
    /// Clears all canvas interactions, unregistering their handlers.
    /// </summary>
    public void ClearCanvasInteractions()
    {
        this.Release(this.CanvasInteractions);
        this.CanvasInteractions = [];
    }

    // Timeline interactions

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
    /// Releases and removes all Timeline interactions.
    /// </summary>
    public void ClearTimelineInteractions()
    {
        foreach (var interactions in this._timelineInteractions.Values)
        {
            this.Release(interactions);
        }

        this._timelineInteractions.Clear();
    }

    // Handler lookup

    /// <summary>
    /// Attempts to find the handler registered under <paramref name="handlerId"/>.
    /// </summary>
    public bool TryGetHandler(Guid handlerId, out Action? handler) =>
        this._registry.TryGet(handlerId, out handler);

    // Teardown

    /// <summary>
    /// Clears all interactions (canvas and timeline) and unregisters all handlers.
    /// Called on session dispose under <c>_stateLock</c>.
    /// </summary>
    public void Clear()
    {
        this._registry.Clear();
        this.CanvasInteractions = [];
        this._timelineInteractions.Clear();
    }

    // Private helpers

    private IReadOnlyList<CommittedInteraction> Commit(
        PendingInteractions interactions,
        int? childIndex
    )
    {
        if (interactions.Count == 0)
        {
            return [];
        }

        var committed = new List<CommittedInteraction>(interactions.Count);
        foreach (var interaction in interactions)
        {
            var target = childIndex is int index
                ? interaction.Target.Prepend(index)
                : interaction.Target;
            var handlerId = this._registry.Register(interaction.Handler);
            committed.Add(
                new CommittedInteraction(
                    target,
                    interaction.Event,
                    handlerId,
                    InteractionState.Live
                )
            );
        }

        return committed;
    }

    private void Release(IEnumerable<CommittedInteraction> interactions) =>
        this._registry.Unregister(interactions.Select(i => i.HandlerId));
}
