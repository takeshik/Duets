namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Tabler card component.
/// </summary>
public sealed record CardOptions
{
    /// <summary>
    /// Gets the optional card header title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the optional rendered card footer content.
    /// </summary>
    public DisplayContent? Footer { get; init; }

    /// <summary>
    /// Gets the optional Tabler color token. Applied as <c>card-{color}</c> on the card border.
    /// </summary>
    public string? Color { get; init; }
}
