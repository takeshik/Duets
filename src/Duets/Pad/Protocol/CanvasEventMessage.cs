using Duets.Pad.Interactions;
using Duets.Pad.State;

namespace Duets.Pad.Protocol;

/// <summary>
/// Server-to-browser Canvas event before JSON serialization.
/// </summary>
internal sealed record CanvasEventMessage
{
    private CanvasEventMessage(
        string type,
        CanvasState state,
        IReadOnlyList<CommittedInteraction> interactions
    )
    {
        this.Type = !string.IsNullOrWhiteSpace(type)
            ? type
            : throw new ArgumentException("Canvas event type cannot be empty.", nameof(type));
        this.State = state ?? throw new ArgumentNullException(nameof(state));
        this.Interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    public string Type { get; }

    public CanvasState State { get; }

    public IReadOnlyList<CommittedInteraction> Interactions { get; }

    public static CanvasEventMessage Snapshot(
        CanvasState state,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new(CanvasEventTypes.Snapshot, state, interactions);

    public static CanvasEventMessage Replace(
        CanvasState state,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new(CanvasEventTypes.Replace, state, interactions);
}
