using System.Collections;

namespace Duets.Pad.Rendering;

internal sealed class PendingInteractions
    : IReadOnlyList<PendingInteraction>,
        IEquatable<PendingInteractions>
{
    public static PendingInteractions Empty { get; } = new([]);

    private readonly PendingInteraction[] interactions;

    public PendingInteractions(IEnumerable<PendingInteraction> interactions)
    {
        if (interactions is null)
        {
            throw new ArgumentNullException(nameof(interactions));
        }

        this.interactions = [.. interactions];
        if (this.interactions.Any(interaction => interaction is null))
        {
            throw new ArgumentException(
                "Pending interactions cannot contain null.",
                nameof(interactions)
            );
        }
    }

    public int Count => this.interactions.Length;

    public PendingInteraction this[int index] => this.interactions[index];

    public PendingInteractions PrependPath(int segment) =>
        this.Count == 0
            ? this
            : new PendingInteractions(this.interactions.Select(i => i.PrependPath(segment)));

    public PendingInteractions PrependPath(params int[] prefix) =>
        this.Count == 0
            ? this
            : new PendingInteractions(this.interactions.Select(i => i.PrependPath(prefix)));

    public static PendingInteractions Merge(IEnumerable<PendingInteractions> sources) =>
        new(sources.SelectMany(source => source));

    public bool Equals(PendingInteractions? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || this.interactions.Length != other.interactions.Length)
        {
            return false;
        }

        for (var i = 0; i < this.interactions.Length; i++)
        {
            if (!Equals(this.interactions[i], other.interactions[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is PendingInteractions other && this.Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var interaction in this.interactions)
        {
            hash.Add(interaction);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<PendingInteraction> GetEnumerator() =>
        ((IEnumerable<PendingInteraction>)this.interactions).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
