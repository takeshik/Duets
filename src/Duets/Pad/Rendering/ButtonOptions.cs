namespace Duets.Pad.Rendering;

public sealed record ButtonOptions
{
    public bool Disabled { get; init; }

    public string? Title { get; init; }

    public string? ClassName { get; init; }
}
