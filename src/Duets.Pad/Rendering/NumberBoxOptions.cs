namespace Duets.Pad.Rendering;

/// <summary>Options for rendering a numeric input.</summary>
public sealed record NumberBoxOptions
{
    /// <summary>Gets the optional form field name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the optional minimum value advertised to the browser control.</summary>
    public double? Min { get; init; }

    /// <summary>Gets the optional maximum value advertised to the browser control.</summary>
    public double? Max { get; init; }

    /// <summary>Gets the optional increment used by the browser control.</summary>
    public double? Step { get; init; }

    /// <summary>Gets whether the input is disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Gets the optional native tooltip text.</summary>
    public string? Title { get; init; }
}
