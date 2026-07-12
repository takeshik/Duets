namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a link component.
/// </summary>
public sealed record LinkOptions
{
    /// <summary>
    /// Gets the optional tooltip title.
    /// </summary>
    public string? Title { get; init; }
}
