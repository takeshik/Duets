namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering code and preformatted text blocks.
/// </summary>
public sealed record PreformattedOptions
{
    /// <summary>
    /// Gets a value indicating whether long lines wrap instead of scrolling horizontally.
    /// </summary>
    public bool Wrap { get; init; }
}
