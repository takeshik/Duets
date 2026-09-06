using Duets.Pad.Rendering;

namespace Duets.Pad.State;

/// <summary>
/// Reduced server-side Canvas tree.
/// </summary>
/// <param name="root">The root element of the Canvas tree.</param>
public sealed class CanvasState(Element root) : IEquatable<CanvasState>
{
    /// <summary>Gets the empty Canvas state.</summary>
    public static CanvasState Empty { get; } = new(CreateEmptyRoot());

    /// <summary>Gets the root element.</summary>
    public Element Root { get; } = root ?? throw new ArgumentNullException(nameof(root));

    /// <summary>Creates a state with a node appended to the root.</summary>
    /// <param name="node">The terminal node to append.</param>
    /// <returns>The updated state.</returns>
    public CanvasState Append(ITerminalRenderNode node)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        return new CanvasState(this.Root.WithChildren(this.Root.Children.Add(node)));
    }

    /// <summary>Creates a state with the root children replaced.</summary>
    /// <param name="children">The replacement child nodes.</param>
    /// <returns>The updated state.</returns>
    public CanvasState Set(ElementChildren children)
    {
        if (children is null)
        {
            throw new ArgumentNullException(nameof(children));
        }

        return new CanvasState(this.Root.WithChildren(children));
    }

    /// <summary>Returns the empty Canvas state.</summary>
    public CanvasState Clear() => Empty;

    /// <inheritdoc />
    public bool Equals(CanvasState? other) =>
        ReferenceEquals(this, other) || (other is not null && this.Root == other.Root);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CanvasState other && this.Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => this.Root.GetHashCode();

    private static Element CreateEmptyRoot() =>
        new(
            "div",
            new ElementAttributes(new KeyValuePair<string, string?>("data-duetspad-root", null))
        );
}
