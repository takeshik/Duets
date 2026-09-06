namespace Duets.Pad.Rendering;

/// <summary>Options for rendering a range slider.</summary>
public sealed record SliderOptions
{
    /// <summary>Gets the optional form field name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the minimum value advertised to the browser control.</summary>
    public double Min { get; init; }

    /// <summary>Gets the maximum value advertised to the browser control. The default is 100.</summary>
    public double Max { get; init; } = 100;

    /// <summary>Gets the optional increment used by the browser control.</summary>
    public double? Step { get; init; }

    /// <summary>Gets whether the slider is disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Gets the optional native tooltip text.</summary>
    public string? Title { get; init; }
}
