namespace Duets.Pad.Rendering;

public sealed record SliderOptions
{
    public string? Name { get; init; }

    public double Min { get; init; }

    public double Max { get; init; } = 100;

    public double? Step { get; init; }

    public bool Disabled { get; init; }

    public string? Title { get; init; }
}
