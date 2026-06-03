using Duets.Pad.Timeline;

namespace Duets.Pad.Protocol;

/// <summary>
/// Server-to-browser Timeline event before JSON serialization.
/// </summary>
internal sealed record TimelineEventMessage
{
    private TimelineEventMessage(string type, TimelineState? state, TimelineEntry? entry)
    {
        this.Type = !string.IsNullOrWhiteSpace(type)
            ? type
            : throw new ArgumentException("Timeline event type cannot be empty.", nameof(type));
        this.State = state;
        this.Entry = entry;
    }

    public string Type { get; }

    public TimelineState? State { get; }

    public TimelineEntry? Entry { get; }

    public static TimelineEventMessage Snapshot(TimelineState state) =>
        new("snapshot", state ?? throw new ArgumentNullException(nameof(state)), entry: null);

    public static TimelineEventMessage Append(TimelineEntry entry) =>
        new("append", state: null, entry ?? throw new ArgumentNullException(nameof(entry)));

    public static TimelineEventMessage Replace(TimelineEntry entry) =>
        new("replace", state: null, entry ?? throw new ArgumentNullException(nameof(entry)));

    public static TimelineEventMessage Clear() => new("clear", state: null, entry: null);
}
