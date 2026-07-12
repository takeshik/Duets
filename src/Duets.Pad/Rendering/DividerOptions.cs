namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a horizontal divider.
/// </summary>
public sealed record DividerOptions
{
    /// <summary>
    /// Gets the optional label text. When set, renders as Tabler's labeled
    /// divider (<c>&lt;div class="hr-text"&gt;</c>) instead of a plain <c>&lt;hr&gt;</c>.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the optional Tabler color token applied as <c>text-{color}</c>.
    /// </summary>
    public string? Color { get; init; }
}
