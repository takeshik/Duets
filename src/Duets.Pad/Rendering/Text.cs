namespace Duets.Pad.Rendering;

/// <summary>
/// Terminal renderable representing a text node.
/// </summary>
public sealed record Text(string Value) : ITerminalRenderNode
{
    public string Value { get; } = Value ?? throw new ArgumentNullException(nameof(Value));

    public bool CanReduce => false;

    public IRenderNode Reduce() => this;
}
