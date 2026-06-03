using System.Collections;

namespace Duets.Pad.Rendering;

/// <summary>
/// Immutable, order-sensitive child list for <see cref="Element" />.
/// </summary>
public sealed class ElementChildren
    : IReadOnlyList<ITerminalRenderNode>,
        IEquatable<ElementChildren>
{
    public static ElementChildren Empty { get; } = new([]);

    private readonly ITerminalRenderNode[] children;

    public ElementChildren(IEnumerable<ITerminalRenderNode> children)
    {
        if (children is null)
        {
            throw new ArgumentNullException(nameof(children));
        }

        this.children = [.. children];

        if (this.children.Any(child => child is null))
        {
            throw new ArgumentException("Element children cannot contain null.", nameof(children));
        }
    }

    public ElementChildren(params ITerminalRenderNode[] children)
        : this((IEnumerable<ITerminalRenderNode>)children) { }

    public int Count => this.children.Length;

    public ITerminalRenderNode this[int index] => this.children[index];

    public ElementChildren Add(ITerminalRenderNode child)
    {
        if (child is null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        var next = new ITerminalRenderNode[this.children.Length + 1];
        Array.Copy(this.children, next, this.children.Length);
        next[^1] = child;
        return [.. next];
    }

    public ElementChildren AddRange(IEnumerable<ITerminalRenderNode> children)
    {
        if (children is null)
        {
            throw new ArgumentNullException(nameof(children));
        }

        return [.. this.children.Concat(children)];
    }

    public bool Equals(ElementChildren? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || this.children.Length != other.children.Length)
        {
            return false;
        }

        for (var i = 0; i < this.children.Length; i++)
        {
            if (!Equals(this.children[i], other.children[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is ElementChildren other && this.Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var child in this.children)
        {
            hash.Add(child);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<ITerminalRenderNode> GetEnumerator() =>
        ((IEnumerable<ITerminalRenderNode>)this.children).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
