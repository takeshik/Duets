namespace Duets.Pad.Protocol;

/// <summary>
/// Discriminated union of all event types sent on the unified <c>GET /sessions/{sessionId}/events</c>
/// SSE stream. Each variant wraps one of the three surface event types.
/// </summary>
internal abstract class PadEventMessage
{
    private protected PadEventMessage() { }

    /// <summary>Wraps a <see cref="CanvasEventMessage"/>.</summary>
    internal sealed class Canvas(CanvasEventMessage message) : PadEventMessage
    {
        public CanvasEventMessage Message { get; } = message;
    }

    /// <summary>Wraps a <see cref="TimelineEventMessage"/>.</summary>
    internal sealed class Timeline(TimelineEventMessage message) : PadEventMessage
    {
        public TimelineEventMessage Message { get; } = message;
    }

    /// <summary>Wraps a <see cref="TypeDeclaration"/>.</summary>
    internal sealed class TypeDeclaration(global::Duets.TypeDeclaration declaration)
        : PadEventMessage
    {
        public global::Duets.TypeDeclaration Declaration { get; } = declaration;
    }
}
