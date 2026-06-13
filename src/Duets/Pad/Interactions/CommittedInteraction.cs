using Duets.Pad.Rendering;

namespace Duets.Pad.Interactions;

internal sealed record CommittedInteraction(
    DisplayPath Target,
    InteractionEvent Event,
    Guid HandlerId,
    InteractionState State
)
{
    public DisplayPath Target { get; } = Target ?? throw new ArgumentNullException(nameof(Target));

    public Guid HandlerId { get; } =
        HandlerId != Guid.Empty
            ? HandlerId
            : throw new ArgumentException("Handler id cannot be empty.", nameof(HandlerId));
}
