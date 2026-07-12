namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Tabler status component.
/// </summary>
public sealed record StatusOptions
{
    /// <summary>
    /// Gets the Tabler status color token.
    /// </summary>
    public string? Color { get; init; }

    /// <summary>
    /// Gets a value indicating whether the status dot is animated.
    /// </summary>
    public bool Animated { get; init; }
}
