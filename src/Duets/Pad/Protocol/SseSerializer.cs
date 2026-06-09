using System.Text.Json.Nodes;
using Duets.Pad.State;
using Duets.Pad.Timeline;

namespace Duets.Pad.Protocol;

/// <summary>
/// Serializes <see cref="CanvasEventMessage"/> and <see cref="TimelineEventMessage"/> to the JSON
/// string placed on an SSE <c>data:</c> line (without the <c>data: </c> prefix).
/// </summary>
internal static class SseSerializer
{
    private static readonly CanvasSerializer _canvasSerializer = new();

    /// <summary>
    /// Serializes a <see cref="CanvasEventMessage"/> to SSE data JSON.
    /// Canvas events emit <c>{ "type": "&lt;canvas.*&gt;", "state": &lt;element-json&gt; }</c>.
    /// </summary>
    public static string Serialize(CanvasEventMessage m)
    {
        if (m is null)
        {
            throw new ArgumentNullException(nameof(m));
        }

        return new JsonObject
        {
            ["type"] = m.Type,
            ["state"] = _canvasSerializer.Serialize(m.State),
        }.ToJsonString();
    }

    /// <summary>
    /// Serializes a <see cref="TimelineEventMessage"/> to SSE data JSON.
    /// <list type="bullet">
    ///   <item>timeline.reset  → <c>{ "type": "timeline.reset",  "reason": "...", "entries": [ ... ] }</c></item>
    ///   <item>timeline.append → <c>{ "type": "timeline.append", "entry":   { ... } }</c></item>
    ///   <item>timeline.update → <c>{ "type": "timeline.update", "entry":   { ... } }</c></item>
    ///   <item>timeline.trim   → <c>{ "type": "timeline.trim",   "removeBeforeId": ..., "marker": ... }</c></item>
    /// </list>
    /// </summary>
    public static string Serialize(TimelineEventMessage m)
    {
        if (m is null)
        {
            throw new ArgumentNullException(nameof(m));
        }

        return m.Type switch
        {
            TimelineEventTypes.Reset => SerializeTimelineReset(m.Type, m.State!, m.Reason!),
            TimelineEventTypes.Append or TimelineEventTypes.Update => SerializeTimelineEntryEvent(
                m.Type,
                m.Entry!
            ),
            TimelineEventTypes.Trim => SerializeTimelineTrim(
                m.Type,
                m.RemoveBeforeId!.Value,
                m.Marker
            ),
            _ => throw new InvalidOperationException(
                $"Unrecognised TimelineEventMessage type '{m.Type}'."
            ),
        };
    }

    private static string SerializeTimelineReset(string type, TimelineState state, string reason)
    {
        var entries = new JsonArray();
        foreach (var entry in state)
        {
            entries.Add(SerializeEntry(entry));
        }

        return new JsonObject
        {
            ["type"] = type,
            ["reason"] = reason,
            ["entries"] = entries,
        }.ToJsonString();
    }

    private static string SerializeTimelineEntryEvent(string type, TimelineEntry entry)
    {
        return new JsonObject { ["type"] = type, ["entry"] = SerializeEntry(entry) }.ToJsonString();
    }

    private static string SerializeTimelineTrim(
        string type,
        long removeBeforeId,
        TimelineEntry? marker
    )
    {
        return new JsonObject
        {
            ["type"] = type,
            ["removeBeforeId"] = removeBeforeId,
            ["marker"] = marker is not null ? SerializeEntry(marker) : null,
        }.ToJsonString();
    }

    private static JsonObject SerializeEntry(TimelineEntry entry)
    {
        return new JsonObject
        {
            ["id"] = entry.Id,
            ["reason"] = entry.Reason,
            ["body"] = Rendering.RenderNodeJsonSerializer.Serialize(entry.Body),
            ["timestamp"] = entry.Timestamp.ToString("O"),
        };
    }
}
