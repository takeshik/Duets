using Duets.Pad.Rendering;

namespace Duets.Pad.Protocol;

/// <summary>
/// Base type for a Canvas incremental patch operation.
/// </summary>
internal abstract record CanvasPatchOperation;

/// <summary>
/// Sets an attribute on an existing element.
/// </summary>
internal sealed record SetAttributeOperation(DisplayPath Path, string Name, string? Value)
    : CanvasPatchOperation
{
    public DisplayPath Path { get; } = Path ?? throw new ArgumentNullException(nameof(Path));

    public string Name { get; } =
        !string.IsNullOrWhiteSpace(Name)
            ? Name
            : throw new ArgumentException("Attribute name cannot be empty.", nameof(Name));
}

/// <summary>
/// Removes an attribute from an existing element.
/// </summary>
internal sealed record RemoveAttributeOperation(DisplayPath Path, string Name)
    : CanvasPatchOperation
{
    public DisplayPath Path { get; } = Path ?? throw new ArgumentNullException(nameof(Path));

    public string Name { get; } =
        !string.IsNullOrWhiteSpace(Name)
            ? Name
            : throw new ArgumentException("Attribute name cannot be empty.", nameof(Name));
}

/// <summary>
/// Replaces the value of an existing text node.
/// </summary>
internal sealed record ReplaceTextOperation(DisplayPath Path, string Value) : CanvasPatchOperation
{
    public DisplayPath Path { get; } = Path ?? throw new ArgumentNullException(nameof(Path));

    public string Value { get; } = Value ?? throw new ArgumentNullException(nameof(Value));
}

/// <summary>
/// Replaces an existing non-root node with a projected subtree.
/// </summary>
internal sealed record ReplaceNodeOperation(DisplayPath Path, ITerminalRenderNode Node)
    : CanvasPatchOperation
{
    public DisplayPath Path { get; } = Path ?? throw new ArgumentNullException(nameof(Path));

    public ITerminalRenderNode Node { get; } =
        Node ?? throw new ArgumentNullException(nameof(Node));
}

/// <summary>
/// Removes a child from an element in the pre-remove child-index space.
/// </summary>
internal sealed record RemoveChildOperation(DisplayPath ParentPath, int Index)
    : CanvasPatchOperation
{
    public DisplayPath ParentPath { get; } =
        ParentPath ?? throw new ArgumentNullException(nameof(ParentPath));

    public int Index { get; } =
        Index >= 0
            ? Index
            : throw new ArgumentOutOfRangeException(
                nameof(Index),
                "Child index must be non-negative."
            );
}

/// <summary>
/// Inserts a projected subtree into an element in the post-remove child-index space.
/// </summary>
internal sealed record InsertChildOperation(
    DisplayPath ParentPath,
    int Index,
    ITerminalRenderNode Node
) : CanvasPatchOperation
{
    public DisplayPath ParentPath { get; } =
        ParentPath ?? throw new ArgumentNullException(nameof(ParentPath));

    public int Index { get; } =
        Index >= 0
            ? Index
            : throw new ArgumentOutOfRangeException(
                nameof(Index),
                "Child index must be non-negative."
            );

    public ITerminalRenderNode Node { get; } =
        Node ?? throw new ArgumentNullException(nameof(Node));
}
