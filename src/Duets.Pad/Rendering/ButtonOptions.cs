namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Tabler button.
/// </summary>
public sealed record ButtonOptions
{
    /// <summary>
    /// Gets whether the button is disabled.
    /// </summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// Gets the optional native tooltip text.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the Tabler color variant. Defaults to <c>primary</c>.
    /// </summary>
    public string Variant { get; init; } = "primary";

    /// <summary>
    /// Gets whether the button uses the outlined treatment for its variant.
    /// </summary>
    public bool Outline { get; init; }

    /// <summary>
    /// Gets the optional Tabler size token: <c>sm</c>, <c>lg</c>, or <c>xl</c>.
    /// </summary>
    public string? Size { get; init; }
}
