using System.Text.Json.Nodes;
using Duets.Pad.Interactions;
using Duets.Pad.Rendering;
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
            ["interactions"] = SerializeInteractions(m.Interactions),
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
            TimelineEventTypes.Reset => SerializeTimelineReset(
                m.Type,
                m.State!,
                m.Reason!,
                m.StateInteractions!
            ),
            TimelineEventTypes.Append or TimelineEventTypes.Update => SerializeTimelineEntryEvent(
                m.Type,
                m.Entry!,
                m.EntryInteractions!
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

    private static string SerializeTimelineReset(
        string type,
        TimelineState state,
        string reason,
        IReadOnlyDictionary<long, IReadOnlyList<CommittedInteraction>> interactions
    )
    {
        var entries = new JsonArray();
        foreach (var entry in state)
        {
            interactions.TryGetValue(entry.Id, out var entryInteractions);
            entries.Add(SerializeEntry(entry, entryInteractions ?? []));
        }

        return new JsonObject
        {
            ["type"] = type,
            ["reason"] = reason,
            ["entries"] = entries,
        }.ToJsonString();
    }

    private static string SerializeTimelineEntryEvent(
        string type,
        TimelineEntry entry,
        IReadOnlyList<CommittedInteraction> interactions
    )
    {
        return new JsonObject
        {
            ["type"] = type,
            ["entry"] = SerializeEntry(entry, interactions),
        }.ToJsonString();
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
            ["marker"] = marker is not null ? SerializeEntry(marker, []) : null,
        }.ToJsonString();
    }

    private static JsonObject SerializeEntry(
        TimelineEntry entry,
        IReadOnlyList<CommittedInteraction> interactions
    )
    {
        return new JsonObject
        {
            ["id"] = entry.Id,
            ["reason"] = entry.Reason,
            ["body"] = Rendering.RenderNodeJsonSerializer.Serialize(entry.Body),
            ["interactions"] = SerializeInteractions(interactions),
            ["timestamp"] = entry.Timestamp.ToString("O"),
        };
    }

    private static JsonArray SerializeInteractions(IReadOnlyList<CommittedInteraction> interactions)
    {
        var array = new JsonArray();
        foreach (var interaction in interactions)
        {
            array.Add(
                new JsonObject
                {
                    ["target"] = SerializePath(interaction.Target),
                    ["event"] = SerializeEvent(interaction.Event),
                    ["handlerId"] = interaction.HandlerId.ToString(),
                    ["state"] = interaction.State == InteractionState.Live ? "live" : "stale",
                }
            );
        }

        return array;
    }

    private static JsonArray SerializePath(DisplayPath path)
    {
        var array = new JsonArray();
        foreach (var segment in path.Segments)
        {
            array.Add(segment);
        }

        return array;
    }

    private static string SerializeEvent(InteractionEvent value) =>
        value switch
        {
            InteractionEvent.Click => "click",
            _ => throw new InvalidOperationException($"Unrecognised interaction event '{value}'."),
        };
}
