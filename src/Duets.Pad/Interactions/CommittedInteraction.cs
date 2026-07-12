using Duets.Pad.Rendering;

namespace Duets.Pad.Interactions;

internal sealed record CommittedInteraction
{
    public CommittedInteraction(
        DisplayPath target,
        InteractionEvent @event,
        Guid handlerId,
        InteractionState state,
        Action? handler = null
    )
    {
        this.Target = target ?? throw new ArgumentNullException(nameof(target));
        this.Event = @event;
        this.HandlerId =
            handlerId != Guid.Empty
                ? handlerId
                : throw new ArgumentException("Handler id cannot be empty.", nameof(handlerId));
        this.State = state;
        this.Handler = handler;
    }

    public DisplayPath Target { get; }

    public InteractionEvent Event { get; }

    public Guid HandlerId { get; }

    public InteractionState State { get; }

    public Action? Handler { get; }
}
