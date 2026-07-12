namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Bootstrap/Tabler grid column.
/// </summary>
public sealed record ColOptions
{
    /// <summary>
    /// Gets the default column span (1–12). When null and no breakpoint is set,
    /// the column uses Bootstrap's auto equal-width (<c>col</c>).
    /// </summary>
    public int? Span { get; init; }

    /// <summary>
    /// Gets the column span at the <c>sm</c> breakpoint.
    /// </summary>
    public int? Sm { get; init; }

    /// <summary>
    /// Gets the column span at the <c>md</c> breakpoint.
    /// </summary>
    public int? Md { get; init; }

    /// <summary>
    /// Gets the column span at the <c>lg</c> breakpoint.
    /// </summary>
    public int? Lg { get; init; }

    /// <summary>
    /// Gets the column span at the <c>xl</c> breakpoint.
    /// </summary>
    public int? Xl { get; init; }
}
