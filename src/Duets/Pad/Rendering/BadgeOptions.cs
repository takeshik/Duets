namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Tabler badge component.
/// </summary>
public sealed record BadgeOptions
{
    /// <summary>
    /// Gets the Tabler color token used as <c>bg-{color}-lt</c>.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Gets a value indicating whether the badge uses Tabler's pill shape.
    /// </summary>
    public bool Pill { get; init; }

    /// <summary>
    /// Gets a value indicating whether the badge uses Tabler's outline style.
    /// </summary>
    public bool Outline { get; init; }
}
