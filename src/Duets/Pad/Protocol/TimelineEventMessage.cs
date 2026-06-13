using Duets.Pad.Interactions;
using Duets.Pad.Timeline;

namespace Duets.Pad.Protocol;

/// <summary>
/// Server-to-browser Timeline event before JSON serialization.
/// </summary>
internal sealed record TimelineEventMessage
{
    private TimelineEventMessage(
        string type,
        TimelineState? state,
        string? reason,
        TimelineEntry? entry,
        IReadOnlyList<CommittedInteraction>? entryInteractions,
        IReadOnlyDictionary<long, IReadOnlyList<CommittedInteraction>>? stateInteractions,
        long? removeBeforeId,
        TimelineEntry? marker
    )
    {
        this.Type = !string.IsNullOrWhiteSpace(type)
            ? type
            : throw new ArgumentException("Timeline event type cannot be empty.", nameof(type));
        this.State = state;
        this.Reason = reason;
        this.Entry = entry;
        this.EntryInteractions = entryInteractions;
        this.StateInteractions = stateInteractions;
        this.RemoveBeforeId = removeBeforeId;
        this.Marker = marker;
    }

    public string Type { get; }

    public TimelineState? State { get; }

    public string? Reason { get; }

    public TimelineEntry? Entry { get; }

    public IReadOnlyList<CommittedInteraction>? EntryInteractions { get; }

    public IReadOnlyDictionary<
        long,
        IReadOnlyList<CommittedInteraction>
    >? StateInteractions { get; }

    public long? RemoveBeforeId { get; }

    public TimelineEntry? Marker { get; }

    public static TimelineEventMessage Reset(
        TimelineState state,
        string reason,
        IReadOnlyDictionary<long, IReadOnlyList<CommittedInteraction>> interactions
    ) =>
        new(
            TimelineEventTypes.Reset,
            state ?? throw new ArgumentNullException(nameof(state)),
            !string.IsNullOrWhiteSpace(reason)
                ? reason
                : throw new ArgumentException("Reset reason cannot be empty.", nameof(reason)),
            entry: null,
            entryInteractions: null,
            stateInteractions: SnapshotInteractions(interactions),
            removeBeforeId: null,
            marker: null
        );

    public static TimelineEventMessage Append(
        TimelineEntry entry,
        IReadOnlyList<CommittedInteraction> interactions
    ) =>
        new(
            TimelineEventTypes.Append,
            state: null,
            reason: null,
            entry ?? throw new ArgumentNullException(nameof(entry)),
            interactions ?? throw new ArgumentNullException(nameof(interactions)),
            stateInteractions: null,
            removeBeforeId: null,
            marker: null
        );

    public static TimelineEventMessage Update(
        TimelineEntry entry,
        IReadOnlyList<CommittedInteraction> interactions
    ) =>
        new(
            TimelineEventTypes.Update,
            state: null,
            reason: null,
            entry ?? throw new ArgumentNullException(nameof(entry)),
            interactions ?? throw new ArgumentNullException(nameof(interactions)),
            stateInteractions: null,
            removeBeforeId: null,
            marker: null
        );

    public static TimelineEventMessage Trim(long removeBeforeId, TimelineEntry? marker) =>
        new(
            TimelineEventTypes.Trim,
            state: null,
            reason: null,
            entry: null,
            entryInteractions: null,
            stateInteractions: null,
            removeBeforeId,
            marker
        );

    private static IReadOnlyDictionary<
        long,
        IReadOnlyList<CommittedInteraction>
    > SnapshotInteractions(
        IReadOnlyDictionary<long, IReadOnlyList<CommittedInteraction>> interactions
    )
    {
        if (interactions is null)
        {
            throw new ArgumentNullException(nameof(interactions));
        }

        return interactions.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<CommittedInteraction>)[.. kv.Value]
        );
    }
}
