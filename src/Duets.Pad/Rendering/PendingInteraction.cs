namespace Duets.Pad.Rendering;

internal sealed record PendingInteraction
{
    public PendingInteraction(DisplayPath target, InteractionEvent @event, Action handler)
    {
        this.Target = target ?? throw new ArgumentNullException(nameof(target));
        this.Event = @event;
        this.Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public DisplayPath Target { get; }

    public InteractionEvent Event { get; }

    public Action Handler { get; }

    public PendingInteraction PrependPath(int segment) =>
        new(this.Target.Prepend(segment), this.Event, this.Handler);

    public PendingInteraction PrependPath(IEnumerable<int> prefix) =>
        new(this.Target.Prepend(prefix), this.Event, this.Handler);
}
