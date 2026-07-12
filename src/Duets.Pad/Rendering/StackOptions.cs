namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a stack container.
/// </summary>
public sealed record StackOptions
{
    /// <summary>
    /// Gets the stack layout direction. Defaults to <c>"vertical"</c>.
    /// </summary>
    public string? Direction { get; init; }
}
