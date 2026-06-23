namespace Duets.Pad.Protocol;

/// <summary>Canonical SSE event-type discriminators for the Canvas stream.</summary>
internal static class CanvasEventTypes
{
    public const string Snapshot = "canvas.snapshot";
    public const string Replace = "canvas.replace";
    public const string Patch = "canvas.patch";
}
