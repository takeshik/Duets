namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Tabler progress component.
/// </summary>
public sealed record ProgressOptions
{
    /// <summary>
    /// Gets the Tabler background color token used as <c>bg-{color}</c> on the bar.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Gets the optional progress bar label.
    /// </summary>
    public string? Label { get; init; }
}
