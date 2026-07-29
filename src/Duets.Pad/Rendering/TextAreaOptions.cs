namespace Duets.Pad.Rendering;

public sealed record TextAreaOptions
{
    public string? Name { get; init; }

    public string? Placeholder { get; init; }

    public int? Rows { get; init; }

    public bool Disabled { get; init; }

    public string? Title { get; init; }
}
