namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Tabler spinner component.
/// </summary>
public sealed record SpinnerOptions
{
    /// <summary>
    /// Gets the Tabler text color token used as <c>text-{color}</c>.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Gets a value indicating whether the spinner uses the small size.
    /// </summary>
    public bool Small { get; init; }
}
