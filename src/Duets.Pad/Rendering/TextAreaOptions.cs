namespace Duets.Pad.Rendering;

/// <summary>Options for rendering a multiline text input.</summary>
public sealed record TextAreaOptions
{
    /// <summary>Gets the optional form field name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the optional placeholder text.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Gets the optional number of visible text rows.</summary>
    public int? Rows { get; init; }

    /// <summary>Gets whether the input is disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Gets the optional native tooltip text.</summary>
    public string? Title { get; init; }
}
