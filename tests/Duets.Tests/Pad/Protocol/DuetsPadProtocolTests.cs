using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Duets.Pad.State;
using Duets.Pad.Timeline;

namespace Duets.Tests.Pad.Protocol;

public sealed class DuetsPadProtocolTests
{
    [Fact]
    public void Canvas_snapshot_event_carries_the_full_canvas_state()
    {
        var state = CanvasState.Empty.Append(new Text("hello"));

        var message = CanvasEventMessage.Snapshot(state);

        Assert.Equal("snapshot", message.Type);
        Assert.Same(state, message.State);
    }

    [Fact]
    public void Timeline_snapshot_event_carries_the_full_timeline_state()
    {
        var state = TimelineState.Empty.Append("dump", new Text("hello"));

        var message = TimelineEventMessage.Snapshot(state);

        Assert.Equal("snapshot", message.Type);
        Assert.Same(state, message.State);
        Assert.Null(message.Entry);
    }

    [Fact]
    public void Timeline_append_event_carries_one_entry()
    {
        var entry = new TimelineEntry(0, "dump", new Text("hello"));

        var message = TimelineEventMessage.Append(entry);

        Assert.Equal("append", message.Type);
        Assert.Null(message.State);
        Assert.Same(entry, message.Entry);
    }

    [Fact]
    public void Timeline_replace_event_carries_one_entry()
    {
        var entry = new TimelineEntry(0, "render-error", new Text("failed"));

        var message = TimelineEventMessage.Replace(entry);

        Assert.Equal("replace", message.Type);
        Assert.Null(message.State);
        Assert.Same(entry, message.Entry);
    }

    [Fact]
    public void Timeline_clear_event_has_no_payload()
    {
        var message = TimelineEventMessage.Clear();

        Assert.Equal("clear", message.Type);
        Assert.Null(message.State);
        Assert.Null(message.Entry);
    }
}
