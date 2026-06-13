namespace Duets.Pad.Interactions;

internal sealed class InteractionRegistry
{
    private readonly Dictionary<Guid, Action> handlers = [];

    public Guid Register(Action handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var id = Guid.NewGuid();
        this.handlers.Add(id, handler);
        return id;
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
