namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a disclosure component.
/// </summary>
public sealed record DisclosureOptions
{
    /// <summary>
    /// Gets a value indicating whether the disclosure is initially open.
    /// </summary>
    public bool Open { get; init; }
}
