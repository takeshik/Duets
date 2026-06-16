using Duets.Pad.Interactions;
using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Duets.Pad.State;
using Duets.Pad.Timeline;

namespace Duets.Tests.Pad.Protocol;

public sealed class DuetsPadProtocolTests
{
    // Canvas events

    [Fact]
    public void Canvas_snapshot_event_type_is_canvas_snapshot_and_carries_state()
    {
        var state = CanvasState.Empty.Append(new Text("hello"));

        var message = CanvasEventMessage.Snapshot("default", state, []);

        Assert.Equal(CanvasEventTypes.Snapshot, message.Type);
        Assert.Same(state, message.State);
        Assert.Empty(message.Interactions);
    }

    [Fact]
    public void Canvas_snapshot_carries_name()
    {
        var message = CanvasEventMessage.Snapshot("default", CanvasState.Empty, []);
        Assert.Equal("default", message.Name);

        var messageNamed = CanvasEventMessage.Snapshot("myCanvas", CanvasState.Empty, []);
        Assert.Equal("myCanvas", messageNamed.Name);
    }

    [Fact]
    public void Canvas_replace_event_type_is_canvas_replace_and_carries_state()
    {
        var state = CanvasState.Empty.Append(new Text("world"));

        var message = CanvasEventMessage.Replace("default", state, []);

        Assert.Equal(CanvasEventTypes.Replace, message.Type);
        Assert.Same(state, message.State);
        Assert.Empty(message.Interactions);
    }

    [Fact]
    public void Canvas_replace_carries_name()
    {
        var message = CanvasEventMessage.Replace("default", CanvasState.Empty, []);
        Assert.Equal("default", message.Name);

        var messageNamed = CanvasEventMessage.Replace("myCanvas", CanvasState.Empty, []);
        Assert.Equal("myCanvas", messageNamed.Name);
    }

    // Timeline events

    [Fact]
    public void Timeline_reset_event_has_type_reason_and_entries()
    {
        var state = TimelineState.Empty.Append("dump", new Text("hello"), DateTimeOffset.MinValue);

        var message = TimelineEventMessage.Reset(
            state,
            "initial",
            new Dictionary<long, IReadOnlyList<CommittedInteraction>>()
        );

        Assert.IsType<ResetMessage>(message);
        Assert.Equal(TimelineEventTypes.Reset, message.Type);
        Assert.Equal("initial", message.Reason);
        Assert.Same(state, message.State);
        Assert.NotNull(message.StateInteractions);
        Assert.Empty(message.StateInteractions);
    }

    [Fact]
    public void Timeline_append_event_carries_one_entry()
    {
        var entry = new TimelineEntry(0, "dump", new Text("hello"), DateTimeOffset.MinValue);

        var message = TimelineEventMessage.Append(entry, []);

        Assert.IsType<AppendMessage>(message);
        Assert.Equal(TimelineEventTypes.Append, message.Type);
        Assert.Same(entry, message.Entry);
        Assert.NotNull(message.EntryInteractions);
        Assert.Empty(message.EntryInteractions);
    }

    [Fact]
    public void Timeline_update_event_carries_one_entry()
    {
        var entry = new TimelineEntry(
            0,
            "render-error",
            new Text("failed"),
            DateTimeOffset.MinValue
        );

        var message = TimelineEventMessage.Update(entry, []);

        Assert.IsType<UpdateMessage>(message);
        Assert.Equal(TimelineEventTypes.Update, message.Type);
        Assert.Same(entry, message.Entry);
        Assert.NotNull(message.EntryInteractions);
        Assert.Empty(message.EntryInteractions);
    }

    [Fact]
    public void Timeline_trim_event_carries_removeBeforeId_and_non_null_marker()
    {
        var marker = new TimelineEntry(
            5,
            "trim-marker",
            new Text("trimmed"),
            DateTimeOffset.MinValue
        );

        var message = TimelineEventMessage.Trim(removeBeforeId: 5, marker);

        Assert.IsType<TrimMessage>(message);
        Assert.Equal(TimelineEventTypes.Trim, message.Type);
        Assert.Equal(5L, message.RemoveBeforeId);
        Assert.Same(marker, message.Marker);
    }

    [Fact]
    public void Timeline_trim_event_marker_may_be_null()
    {
        var message = TimelineEventMessage.Trim(removeBeforeId: 10, marker: null);

        Assert.IsType<TrimMessage>(message);
        Assert.Equal(TimelineEventTypes.Trim, message.Type);
        Assert.Equal(10L, message.RemoveBeforeId);
        Assert.Null(message.Marker);
    }

    // Serializer: Canvas events

    [Fact]
    public void Serializer_canvas_snapshot_emits_namespaced_type()
    {
        var state = CanvasState.Empty;
        var message = CanvasEventMessage.Snapshot("default", state, []);

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{CanvasEventTypes.Snapshot}\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_canvas_replace_emits_namespaced_type()
    {
        var state = CanvasState.Empty;
        var message = CanvasEventMessage.Replace("default", state, []);

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{CanvasEventTypes.Replace}\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_canvas_snapshot_emits_name_field()
    {
        var message = CanvasEventMessage.Snapshot("myCanvas", CanvasState.Empty, []);

        var json = SseSerializer.Serialize(message);

        Assert.Contains("\"name\":\"myCanvas\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_canvas_replace_emits_name_field()
    {
        var message = CanvasEventMessage.Replace("myCanvas", CanvasState.Empty, []);

        var json = SseSerializer.Serialize(message);

        Assert.Contains("\"name\":\"myCanvas\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_canvas_event_emits_interactions_sidecar()
    {
        var handlerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var interaction = new CommittedInteraction(
            new DisplayPath([2, 1]),
            InteractionEvent.Click,
            handlerId,
            InteractionState.Stale
        );
        var message = CanvasEventMessage.Replace("default", CanvasState.Empty, [interaction]);

        var json = SseSerializer.Serialize(message);

        Assert.Contains("\"interactions\"", json, StringComparison.Ordinal);
        Assert.Contains("\"target\":[2,1]", json, StringComparison.Ordinal);
        Assert.Contains("\"event\":\"click\"", json, StringComparison.Ordinal);
        Assert.Contains($"\"handlerId\":\"{handlerId}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"stale\"", json, StringComparison.Ordinal);
    }

    // Serializer: Timeline events

    [Fact]
    public void Serializer_timeline_reset_emits_type_reason_and_entries()
    {
        var state = TimelineState.Empty.Append("dump", new Text("hi"), DateTimeOffset.MinValue);
        var message = TimelineEventMessage.Reset(
            state,
            "initial",
            new Dictionary<long, IReadOnlyList<CommittedInteraction>>()
        );

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{TimelineEventTypes.Reset}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"initial\"", json, StringComparison.Ordinal);
        Assert.Contains("\"entries\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_reset_snapshots_interactions_at_message_creation()
    {
        var handlerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var state = TimelineState.Empty.Append("dump", new Text("hi"), DateTimeOffset.MinValue);
        var interaction = new CommittedInteraction(
            DisplayPath.Root,
            InteractionEvent.Click,
            handlerId,
            InteractionState.Live
        );
        var interactionList = new List<CommittedInteraction> { interaction };
        var interactions = new Dictionary<long, IReadOnlyList<CommittedInteraction>>
        {
            [0] = interactionList,
        };

        var message = TimelineEventMessage.Reset(state, "initial", interactions);
        interactionList.Clear();
        interactions.Clear();

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"handlerId\":\"{handlerId}\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_timeline_append_emits_type_and_entry()
    {
        var entry = new TimelineEntry(0, "dump", new Text("hi"), DateTimeOffset.MinValue);
        var message = TimelineEventMessage.Append(entry, []);

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{TimelineEventTypes.Append}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"entry\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_timeline_update_emits_type_and_entry()
    {
        var entry = new TimelineEntry(0, "render-error", new Text("oops"), DateTimeOffset.MinValue);
        var message = TimelineEventMessage.Update(entry, []);

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{TimelineEventTypes.Update}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"entry\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_timeline_trim_emits_type_removeBeforeId_and_null_marker()
    {
        var message = TimelineEventMessage.Trim(removeBeforeId: 7, marker: null);

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{TimelineEventTypes.Trim}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"removeBeforeId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"marker\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_timeline_trim_emits_non_null_marker()
    {
        var marker = new TimelineEntry(
            7,
            "trim-marker",
            new Text("retained"),
            DateTimeOffset.MinValue
        );
        var message = TimelineEventMessage.Trim(removeBeforeId: 7, marker);

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{TimelineEventTypes.Trim}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"trim-marker\"", json, StringComparison.Ordinal);
    }

    // Serializer: Control events

    [Fact]
    public void Serializer_control_event_emits_control_namespaced_type()
    {
        var msg = new PadEventMessage.Control("reset", new Dictionary<string, object?>());

        var json = SseSerializer.Serialize(msg);

        Assert.Contains("\"control.reset\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_control_event_merges_payload_fields_into_top_level()
    {
        var payload = new Dictionary<string, object?>
        {
            ["reason"] = "user-requested",
            ["count"] = 42,
        };
        var msg = new PadEventMessage.Control("openText", payload);

        var json = SseSerializer.Serialize(msg);

        Assert.Contains("\"control.openText\"", json, StringComparison.Ordinal);
        Assert.Contains("\"reason\"", json, StringComparison.Ordinal);
        Assert.Contains("\"user-requested\"", json, StringComparison.Ordinal);
        Assert.Contains("\"count\"", json, StringComparison.Ordinal);
        Assert.Contains("42", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_control_event_with_empty_payload_emits_only_type()
    {
        var msg = new PadEventMessage.Control("ping", new Dictionary<string, object?>());

        var json = SseSerializer.Serialize(msg);

        Assert.Contains("\"control.ping\"", json, StringComparison.Ordinal);
        // Only the "type" key should be present.
        Assert.Contains("\"type\"", json, StringComparison.Ordinal);
    }

    // ControlEventTypes helpers

    [Fact]
    public void ControlEventTypes_Make_prepends_control_prefix()
    {
        Assert.Equal("control.reset", ControlEventTypes.Make("reset"));
        Assert.Equal("control.openText", ControlEventTypes.Make("openText"));
    }

    [Fact]
    public void ControlEventTypes_Prefix_is_control_dot()
    {
        Assert.Equal("control.", ControlEventTypes.Prefix);
    }
}
