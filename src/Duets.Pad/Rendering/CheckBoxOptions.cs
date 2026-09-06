namespace Duets.Pad.Rendering;

/// <summary>Options for rendering a checkbox input.</summary>
public sealed record CheckBoxOptions
{
    /// <summary>Gets the optional label shown beside the checkbox.</summary>
    public string? Label { get; init; }

    /// <summary>Gets whether the checkbox is disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Gets the optional native tooltip text.</summary>
    public string? Title { get; init; }
}
