namespace Duets.Pad.Rendering;

public sealed record CheckBoxOptions
{
    public string? Label { get; init; }

    public bool Disabled { get; init; }

    public string? Title { get; init; }

    public string? ClassName { get; init; }
}
