namespace Duets.Pad;

internal sealed record ToastOptions(string? Title, string Variant, int DurationMilliseconds)
{
    public const string DefaultVariant = "info";
    public const int DefaultDurationMilliseconds = 5000;
    public const int MaximumDurationMilliseconds = 600_000;

    public static IReadOnlyList<string> SupportedVariants { get; } =
    ["info", "success", "warning", "danger"];
}
