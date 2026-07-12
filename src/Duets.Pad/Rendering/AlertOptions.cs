namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Tabler alert component.
/// </summary>
public sealed record AlertOptions
{
    /// <summary>
    /// Gets the Tabler alert variant. Defaults to <c>info</c>.
    /// </summary>
    public string? Variant { get; init; }

    /// <summary>
    /// Gets the optional alert title.
    /// </summary>
    public string? Title { get; init; }
}
