namespace Duets.Pad.Rendering;

public sealed record DropDownOptions
{
    public IReadOnlyList<FieldOption> Items { get; init; } = [];

    public string? Name { get; init; }

    public bool Disabled { get; init; }

    public string? Title { get; init; }

    public string? ClassName { get; init; }
}
