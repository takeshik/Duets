using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Duets.Pad.Interactions;
using Duets.Pad.Rendering;
using Duets.Pad.State;
using Duets.Pad.Timeline;

namespace Duets.Pad.Protocol;

/// <summary>
/// Serializes <see cref="CanvasEventMessage"/>, <see cref="TimelineEventMessage"/>, and
/// <see cref="PadEventMessage"/> to the JSON string placed on an SSE <c>data:</c> line (without
/// the <c>data: </c> prefix).
/// </summary>
internal static class SseSerializer
{
    private static readonly CanvasSerializer _canvasSerializer = new();

    /// <summary>
    /// Serializes a <see cref="CanvasEventMessage"/> to SSE data JSON.
    /// </summary>
    public static string Serialize(CanvasEventMessage m)
    {
        if (m is null)
        {
            throw new ArgumentNullException(nameof(m));
        }

        var obj = new JsonObject
        {
            ["type"] = m.Type,
            ["name"] = m.Name,
            ["revision"] = m.Revision,
            ["interactions"] = SerializeInteractions(m.Interactions),
        };

        if (m.Type == CanvasEventTypes.Patch)
        {
            obj["baseRevision"] = m.BaseRevision;
            obj["operations"] = SerializePatchOperations(m.Operations);
        }
        else
        {
            obj["state"] = _canvasSerializer.Serialize(m.State);
        }

        return obj.ToJsonString();
    }

    private static JsonArray SerializePatchOperations(
        IReadOnlyList<CanvasPatchOperation> operations
    )
    {
        var array = new JsonArray();
        foreach (var operation in operations)
        {
            array.Add(SerializePatchOperation(operation));
        }

        return array;
    }

    private static JsonObject SerializePatchOperation(CanvasPatchOperation operation) =>
        operation switch
        {
            SetAttributeOperation op => new JsonObject
            {
                ["op"] = "set-attr",
                ["path"] = SerializePath(op.Path),
                ["name"] = op.Name,
                ["value"] = op.Value is not null ? JsonValue.Create(op.Value) : null,
            },
            RemoveAttributeOperation op => new JsonObject
            {
                ["op"] = "remove-attr",
                ["path"] = SerializePath(op.Path),
                ["name"] = op.Name,
            },
            ReplaceTextOperation op => new JsonObject
            {
                ["op"] = "replace-text",
                ["path"] = SerializePath(op.Path),
                ["value"] = op.Value,
            },
            ReplaceNodeOperation op => new JsonObject
            {
                ["op"] = "replace-node",
                ["path"] = SerializePath(op.Path),
                ["node"] = RenderNodeJsonSerializer.Serialize(op.Node),
            },
            RemoveChildOperation op => new JsonObject
            {
                ["op"] = "remove-child",
                ["parentPath"] = SerializePath(op.ParentPath),
                ["index"] = op.Index,
            },
            InsertChildOperation op => new JsonObject
            {
                ["op"] = "insert-child",
                ["parentPath"] = SerializePath(op.ParentPath),
                ["index"] = op.Index,
                ["node"] = RenderNodeJsonSerializer.Serialize(op.Node),
            },
            _ => throw new InvalidOperationException(
                $"Unrecognised CanvasPatchOperation type '{operation.GetType().Name}'."
            ),
        };

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

        return m switch
        {
            ResetMessage r => SerializeTimelineReset(
                r.Type,
                r.State,
                r.Reason,
                r.StateInteractions
            ),
            EntryEventMessage e => SerializeTimelineEntryEvent(
                e.Type,
                e.Entry,
                e.EntryInteractions
            ),
            TrimMessage t => SerializeTimelineTrim(t.Type, t.RemoveBeforeId, t.Marker),
            _ => throw new InvalidOperationException(
                $"Unrecognised TimelineEventMessage type '{m.Type}'."
            ),
        };
    }

    /// <summary>
    /// Serializes a <see cref="PadEventMessage"/> to SSE data JSON by dispatching to the
    /// appropriate typed overload.
    /// </summary>
    public static string Serialize(PadEventMessage m) =>
        m switch
        {
            PadEventMessage.Canvas c => Serialize(c.Message),
            PadEventMessage.Timeline t => Serialize(t.Message),
            PadEventMessage.TypeDeclaration d => SerializeTypeDeclaration(d.Declaration),
            PadEventMessage.TaggedTemplateSnapshot s => SerializeTaggedTemplateSnapshot(s.Snapshot),
            PadEventMessage.Control ctrl => SerializeControl(ctrl.Op, ctrl.Payload),
            _ => throw new InvalidOperationException(
                $"Unrecognised PadEventMessage type '{m.GetType().Name}'."
            ),
        };

    private static string SerializeControl(string op, IReadOnlyDictionary<string, object?> payload)
    {
        var obj = new JsonObject { ["type"] = ControlEventTypes.Make(op) };
        foreach (var (key, value) in payload)
        {
            obj[key] = JsonValue.Create(value);
        }

        return obj.ToJsonString();
    }

    private static string SerializeTypeDeclaration(TypeDeclaration decl) =>
        new JsonObject
        {
            ["type"] = TypeDeclarationEventTypes.Declaration,
            ["fileName"] = decl.FileName,
            ["content"] = decl.Content,
        }.ToJsonString();

    private static string SerializeTaggedTemplateSnapshot(
        Completions.TaggedTemplateRegistrySnapshot snapshot
    )
    {
        var json = new StringBuilder();
        json.Append("{\"type\":\"taggedTemplate.snapshot\",\"version\":");
        json.Append(snapshot.Version);
        json.Append(",\"tags\":[");

        for (var i = 0; i < snapshot.Tags.Count; i++)
        {
            if (i > 0)
            {
                json.Append(',');
            }

            AppendJsonString(json, snapshot.Tags[i]);
        }

        json.Append("]}");
        return json.ToString();
    }

    private static void AppendJsonString(StringBuilder json, string value)
    {
        json.Append('"');
        json.Append(JavaScriptEncoder.Default.Encode(value));
        json.Append('"');
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
                    ["state"] = interaction.State switch
                    {
                        InteractionState.Live => "live",
                        InteractionState.Stale => "stale",
                        _ => throw new InvalidOperationException(
                            $"Unrecognised interaction state '{interaction.State}'."
                        ),
                    },
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
