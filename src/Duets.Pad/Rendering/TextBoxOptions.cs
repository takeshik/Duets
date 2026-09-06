namespace Duets.Pad.Rendering;

/// <summary>Options for rendering a single-line text input.</summary>
public sealed record TextBoxOptions
{
    /// <summary>Gets the optional form field name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the optional placeholder text.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Gets whether the input is disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Gets the optional native tooltip text.</summary>
    public string? Title { get; init; }
}
