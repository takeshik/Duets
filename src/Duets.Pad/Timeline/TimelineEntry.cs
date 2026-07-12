using Duets.Pad.Rendering;

namespace Duets.Pad.Timeline;

/// <summary>
/// A single structured history entry in a DuetsPad Timeline.
/// </summary>
public sealed record TimelineEntry(
    long Id,
    string Reason,
    ITerminalRenderNode Body,
    DateTimeOffset Timestamp
)
{
    public long Id { get; } = Id >= 0 ? Id : throw new ArgumentOutOfRangeException(nameof(Id));

    public string Reason { get; } =
        !string.IsNullOrWhiteSpace(Reason)
            ? Reason
            : throw new ArgumentException("Timeline entry reason cannot be empty.", nameof(Reason));

    public ITerminalRenderNode Body { get; } =
        Body ?? throw new ArgumentNullException(nameof(Body));

    public DateTimeOffset Timestamp { get; } = Timestamp;
}
