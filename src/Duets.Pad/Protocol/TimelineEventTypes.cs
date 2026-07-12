namespace Duets.Pad.Protocol;

/// <summary>Canonical SSE event-type discriminators for the Timeline stream.</summary>
internal static class TimelineEventTypes
{
    public const string Reset = "timeline.reset";
    public const string Append = "timeline.append";
    public const string Update = "timeline.update";
    public const string Trim = "timeline.trim";
}
