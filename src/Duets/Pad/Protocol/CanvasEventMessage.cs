using Duets.Pad.State;

namespace Duets.Pad.Protocol;

/// <summary>
/// Server-to-browser Canvas event before JSON serialization.
/// </summary>
internal sealed record CanvasEventMessage
{
    private CanvasEventMessage(string type, CanvasState state)
    {
        this.Type = !string.IsNullOrWhiteSpace(type)
            ? type
            : throw new ArgumentException("Canvas event type cannot be empty.", nameof(type));
        this.State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public string Type { get; }

    public CanvasState State { get; }

    public static CanvasEventMessage Snapshot(CanvasState state) =>
        new(CanvasEventTypes.Snapshot, state);

    public static CanvasEventMessage Replace(CanvasState state) =>
        new(CanvasEventTypes.Replace, state);
}
