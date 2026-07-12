using Duets.Pad.Interactions;
using Duets.Pad.Timeline;

namespace Duets.Pad.Protocol;

/// <summary>
/// Server-to-browser Timeline event before JSON serialization.
/// </summary>
internal abstract record TimelineEventMessage
{
    private protected TimelineEventMessage(string type)
    {
        this.Type = !string.IsNullOrWhiteSpace(type)
            ? type
            : throw new ArgumentException("Timeline event type cannot be empty.", nameof(type));
    }

    public string Type { get; }

    public static ResetMessage Reset(
        TimelineState state,
        string reason,
        IReadOnlyDictionary<long, IReadOnlyList<CommittedInteraction>> interactions
    ) =>
        new(
            state ?? throw new ArgumentNullException(nameof(state)),
            !string.IsNullOrWhiteSpace(reason)
                ? reason
                : throw new ArgumentException("Reset reason cannot be empty.", nameof(reason)),
            SnapshotInteractions(interactions)
        );

    public static AppendMessage Append(
        TimelineEntry entry,
        IReadOnlyList<CommittedInteraction> interactions
    ) =>
        new(
            entry ?? throw new ArgumentNullException(nameof(entry)),
            interactions ?? throw new ArgumentNullException(nameof(interactions))
        );

    public static UpdateMessage Update(
        TimelineEntry entry,
        IReadOnlyList<CommittedInteraction> interactions
    ) =>
        new(
            entry ?? throw new ArgumentNullException(nameof(entry)),
            interactions ?? throw new ArgumentNullException(nameof(interactions))
        );

    public static TrimMessage Trim(long removeBeforeId, TimelineEntry? marker) =>
        new(removeBeforeId, marker);

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

/// <summary>
/// <c>timeline.reset</c> event: carries the full current Timeline state and per-entry interactions.
/// </summary>
internal sealed record ResetMessage : TimelineEventMessage
{
    internal ResetMessage(
        TimelineState state,
        string reason,
        IReadOnlyDictionary<long, IReadOnlyList<CommittedInteraction>> stateInteractions
    )
        : base(TimelineEventTypes.Reset)
    {
        this.State = state;
        this.Reason = reason;
        this.StateInteractions = stateInteractions;
    }

    public TimelineState State { get; }

    public string Reason { get; }

    public IReadOnlyDictionary<long, IReadOnlyList<CommittedInteraction>> StateInteractions { get; }
}

/// <summary>
/// Base for <c>timeline.append</c> and <c>timeline.update</c> events, both of which
/// carry a single Timeline entry and its associated interactions.
/// </summary>
internal abstract record EntryEventMessage : TimelineEventMessage
{
    private protected EntryEventMessage(
        string type,
        TimelineEntry entry,
        IReadOnlyList<CommittedInteraction> entryInteractions
    )
        : base(type)
    {
        this.Entry = entry;
        this.EntryInteractions = entryInteractions;
    }

    public TimelineEntry Entry { get; }

    public IReadOnlyList<CommittedInteraction> EntryInteractions { get; }
}

/// <summary>
/// <c>timeline.append</c> event: carries a newly appended Timeline entry.
/// </summary>
internal sealed record AppendMessage : EntryEventMessage
{
    internal AppendMessage(
        TimelineEntry entry,
        IReadOnlyList<CommittedInteraction> entryInteractions
    )
        : base(TimelineEventTypes.Append, entry, entryInteractions) { }
}

/// <summary>
/// <c>timeline.update</c> event: carries an updated Timeline entry.
/// </summary>
internal sealed record UpdateMessage : EntryEventMessage
{
    internal UpdateMessage(
        TimelineEntry entry,
        IReadOnlyList<CommittedInteraction> entryInteractions
    )
        : base(TimelineEventTypes.Update, entry, entryInteractions) { }
}

/// <summary>
/// <c>timeline.trim</c> event: indicates that entries before <see cref="RemoveBeforeId"/>
/// have been discarded, optionally accompanied by a replacement marker entry.
/// </summary>
internal sealed record TrimMessage : TimelineEventMessage
{
    internal TrimMessage(long removeBeforeId, TimelineEntry? marker)
        : base(TimelineEventTypes.Trim)
    {
        this.RemoveBeforeId = removeBeforeId;
        this.Marker = marker;
    }

    public long RemoveBeforeId { get; }

    public TimelineEntry? Marker { get; }
}
