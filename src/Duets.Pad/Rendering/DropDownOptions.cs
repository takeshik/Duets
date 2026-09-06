namespace Duets.Pad.Rendering;

/// <summary>Options for rendering a drop-down input.</summary>
public sealed record DropDownOptions
{
    /// <summary>Gets the selectable items.</summary>
    public IReadOnlyList<FieldOption> Items { get; init; } = [];

    /// <summary>Gets the optional form field name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets whether the drop-down is disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Gets the optional native tooltip text.</summary>
    public string? Title { get; init; }
}
