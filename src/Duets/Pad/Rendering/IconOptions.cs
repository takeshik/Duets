namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Tabler icon component.
/// </summary>
public sealed record IconOptions
{
    /// <summary>
    /// Gets the icon font size in pixels.
    /// </summary>
    public double? Size { get; init; }

    /// <summary>
    /// Gets the Tabler text color token used as <c>text-{color}</c>.
    /// </summary>
    public string? Color { get; init; }
}
