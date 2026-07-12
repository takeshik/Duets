namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Bootstrap/Tabler grid row container.
/// </summary>
public sealed record RowOptions
{
    /// <summary>
    /// Gets the gutter size (0–5), mapped to Tabler's <c>g-{n}</c> utility class.
    /// The JS-side aliases <c>"sm"</c>/<c>"md"</c>/<c>"lg"</c> are resolved to
    /// numbers at the script boundary before reaching this record.
    /// </summary>
    public int? Gutter { get; init; }
}
