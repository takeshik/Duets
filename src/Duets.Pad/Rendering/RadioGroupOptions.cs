namespace Duets.Pad.Rendering;

/// <summary>Options for rendering a radio-button group.</summary>
public sealed record RadioGroupOptions
{
    /// <summary>Gets the selectable items.</summary>
    public IReadOnlyList<FieldOption> Items { get; init; } = [];

    /// <summary>Gets the optional form field name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets whether the group is disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Gets the optional native tooltip text.</summary>
    public string? Title { get; init; }
}
