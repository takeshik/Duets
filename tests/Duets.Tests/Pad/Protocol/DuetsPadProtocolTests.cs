using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Duets.Pad.State;
using Duets.Pad.Timeline;

namespace Duets.Tests.Pad.Protocol;

public sealed class DuetsPadProtocolTests
{
    // -------------------------------------------------------------------------
    // Canvas events
    // -------------------------------------------------------------------------

    [Fact]
    public void Canvas_snapshot_event_type_is_canvas_snapshot_and_carries_state()
    {
        var state = CanvasState.Empty.Append(new Text("hello"));

        var message = CanvasEventMessage.Snapshot(state);

        Assert.Equal(CanvasEventTypes.Snapshot, message.Type);
        Assert.Same(state, message.State);
    }

    [Fact]
    public void Canvas_replace_event_type_is_canvas_replace_and_carries_state()
    {
        var state = CanvasState.Empty.Append(new Text("world"));

        var message = CanvasEventMessage.Replace(state);

        Assert.Equal(CanvasEventTypes.Replace, message.Type);
        Assert.Same(state, message.State);
    }

    // -------------------------------------------------------------------------
    // Timeline events
    // -------------------------------------------------------------------------

    [Fact]
    public void Timeline_reset_event_has_type_reason_and_entries()
    {
        var state = TimelineState.Empty.Append("dump", new Text("hello"));

        var message = TimelineEventMessage.Reset(state, "initial");

        Assert.Equal(TimelineEventTypes.Reset, message.Type);
        Assert.Equal("initial", message.Reason);
        Assert.Same(state, message.State);
        Assert.Null(message.Entry);
        Assert.Null(message.RemoveBeforeId);
        Assert.Null(message.Marker);
    }

    [Fact]
    public void Timeline_append_event_carries_one_entry()
    {
        var entry = new TimelineEntry(0, "dump", new Text("hello"));

        var message = TimelineEventMessage.Append(entry);

        Assert.Equal(TimelineEventTypes.Append, message.Type);
        Assert.Null(message.State);
        Assert.Null(message.Reason);
        Assert.Same(entry, message.Entry);
        Assert.Null(message.RemoveBeforeId);
        Assert.Null(message.Marker);
    }

    [Fact]
    public void Timeline_update_event_carries_one_entry()
    {
        var entry = new TimelineEntry(0, "render-error", new Text("failed"));

        var message = TimelineEventMessage.Update(entry);

        Assert.Equal(TimelineEventTypes.Update, message.Type);
        Assert.Null(message.State);
        Assert.Null(message.Reason);
        Assert.Same(entry, message.Entry);
        Assert.Null(message.RemoveBeforeId);
        Assert.Null(message.Marker);
    }

    [Fact]
    public void Timeline_trim_event_carries_removeBeforeId_and_non_null_marker()
    {
        var marker = new TimelineEntry(5, "trim-marker", new Text("trimmed"));

        var message = TimelineEventMessage.Trim(removeBeforeId: 5, marker);

        Assert.Equal(TimelineEventTypes.Trim, message.Type);
        Assert.Null(message.State);
        Assert.Null(message.Reason);
        Assert.Null(message.Entry);
        Assert.Equal(5L, message.RemoveBeforeId);
        Assert.Same(marker, message.Marker);
    }

    [Fact]
    public void Timeline_trim_event_marker_may_be_null()
    {
        var message = TimelineEventMessage.Trim(removeBeforeId: 10, marker: null);

        Assert.Equal(TimelineEventTypes.Trim, message.Type);
        Assert.Equal(10L, message.RemoveBeforeId);
        Assert.Null(message.Marker);
    }

    // -------------------------------------------------------------------------
    // Serializer: Canvas events
    // -------------------------------------------------------------------------

    [Fact]
    public void Serializer_canvas_snapshot_emits_namespaced_type()
    {
        var state = CanvasState.Empty;
        var message = CanvasEventMessage.Snapshot(state);

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{CanvasEventTypes.Snapshot}\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_canvas_replace_emits_namespaced_type()
    {
        var state = CanvasState.Empty;
        var message = CanvasEventMessage.Replace(state);

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{CanvasEventTypes.Replace}\"", json, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // Serializer: Timeline events
    // -------------------------------------------------------------------------

    [Fact]
    public void Serializer_timeline_reset_emits_type_reason_and_entries()
    {
        var state = TimelineState.Empty.Append("dump", new Text("hi"));
        var message = TimelineEventMessage.Reset(state, "initial");

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{TimelineEventTypes.Reset}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"initial\"", json, StringComparison.Ordinal);
        Assert.Contains("\"entries\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_timeline_append_emits_type_and_entry()
    {
        var entry = new TimelineEntry(0, "dump", new Text("hi"));
        var message = TimelineEventMessage.Append(entry);

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{TimelineEventTypes.Append}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"entry\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_timeline_update_emits_type_and_entry()
    {
        var entry = new TimelineEntry(0, "render-error", new Text("oops"));
        var message = TimelineEventMessage.Update(entry);

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
        var marker = new TimelineEntry(7, "trim-marker", new Text("retained"));
        var message = TimelineEventMessage.Trim(removeBeforeId: 7, marker);

        var json = SseSerializer.Serialize(message);

        Assert.Contains($"\"{TimelineEventTypes.Trim}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"trim-marker\"", json, StringComparison.Ordinal);
    }
}
