namespace Duets.Pad.Rendering;

/// <summary>
/// Terminal renderable for HTML that is emitted without escaping or sanitization.
/// </summary>
/// <remarks>Callers must ensure that the supplied content is trusted.</remarks>
/// <param name="Content">The raw HTML content.</param>
public sealed record RawHtml(string Content) : ITerminalRenderNode
{
    /// <summary>Gets the raw HTML content.</summary>
    public string Content { get; } = Content ?? throw new ArgumentNullException(nameof(Content));

    /// <inheritdoc />
    public bool CanReduce => false;

    /// <inheritdoc />
    public IRenderNode Reduce() => this;
}
