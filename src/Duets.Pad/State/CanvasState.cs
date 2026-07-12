using Duets.Pad.Rendering;

namespace Duets.Pad.State;

/// <summary>
/// Reduced server-side Canvas tree.
/// </summary>
public sealed class CanvasState(Element root) : IEquatable<CanvasState>
{
    public static CanvasState Empty { get; } = new(CreateEmptyRoot());

    public Element Root { get; } = root ?? throw new ArgumentNullException(nameof(root));

    public CanvasState Append(ITerminalRenderNode node)
    {
        if (node is null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        return new CanvasState(this.Root.WithChildren(this.Root.Children.Add(node)));
    }

    public CanvasState Set(ElementChildren children)
    {
        if (children is null)
        {
            throw new ArgumentNullException(nameof(children));
        }

        return new CanvasState(this.Root.WithChildren(children));
    }

    public CanvasState Clear() => Empty;

    public bool Equals(CanvasState? other) =>
        ReferenceEquals(this, other) || (other is not null && this.Root == other.Root);

    public override bool Equals(object? obj) => obj is CanvasState other && this.Equals(other);

    public override int GetHashCode() => this.Root.GetHashCode();

    private static Element CreateEmptyRoot() =>
        new(
            "div",
            new ElementAttributes(new KeyValuePair<string, string?>("data-duetspad-root", null))
        );
}
