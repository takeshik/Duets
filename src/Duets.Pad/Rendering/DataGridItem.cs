namespace Duets.Pad.Rendering;

/// <summary>
/// One labeled value displayed by a Tabler data grid.
/// </summary>
public sealed record DataGridItem(string Label, DisplayContent Content)
{
    /// <summary>
    /// Gets the label shown above the item content.
    /// </summary>
    public string Label { get; } = Label ?? throw new ArgumentNullException(nameof(Label));

    /// <summary>
    /// Gets the rendered item content.
    /// </summary>
    public DisplayContent Content { get; } =
        Content ?? throw new ArgumentNullException(nameof(Content));
}
