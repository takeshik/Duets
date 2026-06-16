using Duets.Pad.Interactions;
using Duets.Pad.State;

namespace Duets.Pad.Protocol;

/// <summary>
/// Server-to-browser Canvas event before JSON serialization.
/// </summary>
internal sealed record CanvasEventMessage
{
    private CanvasEventMessage(
        string name,
        string type,
        CanvasState state,
        IReadOnlyList<CommittedInteraction> interactions
    )
    {
        this.Name = !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new ArgumentException("Canvas name cannot be empty.", nameof(name));
        this.Type = !string.IsNullOrWhiteSpace(type)
            ? type
            : throw new ArgumentException("Canvas event type cannot be empty.", nameof(type));
        this.State = state ?? throw new ArgumentNullException(nameof(state));
        this.Interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    }

    public string Name { get; }

    public string Type { get; }

    public CanvasState State { get; }

    public IReadOnlyList<CommittedInteraction> Interactions { get; }

    public static CanvasEventMessage Snapshot(
        string name,
        CanvasState state,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new(name, CanvasEventTypes.Snapshot, state, interactions);

    public static CanvasEventMessage Replace(
        string name,
        CanvasState state,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new(name, CanvasEventTypes.Replace, state, interactions);
}
