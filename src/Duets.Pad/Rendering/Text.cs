namespace Duets.Pad.Rendering;

/// <summary>
/// Terminal renderable representing a text node.
/// </summary>
/// <param name="Value">The literal text content.</param>
public sealed record Text(string Value) : ITerminalRenderNode
{
    /// <summary>Gets the literal text content.</summary>
    public string Value { get; } = Value ?? throw new ArgumentNullException(nameof(Value));

    /// <inheritdoc />
    public bool CanReduce => false;

    /// <inheritdoc />
    public IRenderNode Reduce() => this;
}
