using System.Collections;
using System.Runtime.CompilerServices;

namespace Duets.Pad.Rendering;

/// <summary>
/// Immutable, order-sensitive child list for <see cref="Element" />.
/// </summary>
[CollectionBuilder(typeof(ElementChildren), nameof(Create))]
public sealed class ElementChildren
    : IReadOnlyList<ITerminalRenderNode>,
        IEquatable<ElementChildren>
{
    /// <summary>Gets an empty child list.</summary>
    public static ElementChildren Empty { get; } = new([]);

    /// <summary>Creates a child list from a span for collection-expression support.</summary>
    /// <param name="items">The terminal child nodes.</param>
    /// <returns>The immutable child list.</returns>
    public static ElementChildren Create(ReadOnlySpan<ITerminalRenderNode> items) =>
        new(items.ToArray());

    private readonly ITerminalRenderNode[] children;

    /// <summary>Creates a child list from a sequence.</summary>
    /// <param name="children">The terminal child nodes.</param>
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

    /// <summary>Creates a child list from an array.</summary>
    /// <param name="children">The terminal child nodes.</param>
    public ElementChildren(params ITerminalRenderNode[] children)
        : this((IEnumerable<ITerminalRenderNode>)children) { }

    /// <inheritdoc />
    public int Count => this.children.Length;

    /// <inheritdoc />
    public ITerminalRenderNode this[int index] => this.children[index];

    /// <summary>Creates a child list with one node appended.</summary>
    /// <param name="child">The node to append.</param>
    /// <returns>The updated child list.</returns>
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ElementChildren other && this.Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var child in this.children)
        {
            hash.Add(child);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public IEnumerator<ITerminalRenderNode> GetEnumerator() =>
        ((IEnumerable<ITerminalRenderNode>)this.children).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
