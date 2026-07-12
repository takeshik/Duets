namespace Duets.Pad.Rendering;

public sealed record TextBoxOptions
{
    public string? Name { get; init; }

    public string? Placeholder { get; init; }

    public bool Disabled { get; init; }

    public string? Title { get; init; }

    public string? ClassName { get; init; }
}
