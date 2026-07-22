namespace Duets.Pad.Rendering;

/// <summary>
/// Options for rendering a Tabler empty-space component.
/// </summary>
public sealed record EmptySpaceOptions
{
    /// <summary>
    /// Gets the optional explanatory message shown below the title.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the optional Tabler icon name shown above the title.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Gets the optional action content shown below the message.
    /// </summary>
    public DisplayContent? Action { get; init; }
}
