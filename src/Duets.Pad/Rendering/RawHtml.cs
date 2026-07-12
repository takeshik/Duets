namespace Duets.Pad.Rendering;

/// <summary>
/// Terminal renderable for content that cannot reasonably be represented as structured nodes.
/// </summary>
public sealed record RawHtml(string Content) : ITerminalRenderNode
{
    public string Content { get; } = Content ?? throw new ArgumentNullException(nameof(Content));

    public bool CanReduce => false;

    public IRenderNode Reduce() => this;
}
