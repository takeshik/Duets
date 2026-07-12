namespace Duets.Pad.Interactions;

internal sealed class InteractionRegistry
{
    private readonly Dictionary<Guid, Action> handlers = [];

    public PreparedInteractionRegistration Prepare(Action handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        return new PreparedInteractionRegistration(Guid.NewGuid(), handler);
    }

    public void Commit(PreparedInteractionRegistration registration)
    {
        if (registration is null)
        {
            throw new ArgumentNullException(nameof(registration));
        }

        this.handlers.Add(registration.HandlerId, registration.Handler);
    }

    public bool TryGet(Guid id, out Action? handler) => this.handlers.TryGetValue(id, out handler);

    public void Unregister(Guid id) => this.handlers.Remove(id);

    public void Unregister(IEnumerable<Guid> ids)
    {
        if (ids is null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        foreach (var id in ids)
        {
            this.Unregister(id);
        }
    }

    public void Clear() => this.handlers.Clear();
}

internal sealed class PreparedInteractionRegistration(Guid handlerId, Action handler)
{
    public Guid HandlerId { get; } =
        handlerId != Guid.Empty
            ? handlerId
            : throw new ArgumentException("Handler id cannot be empty.", nameof(handlerId));

    public Action Handler { get; } = handler ?? throw new ArgumentNullException(nameof(handler));
}
