using Duets.Pad.Rendering;
using Duets.Pad.Timeline;

namespace Duets.Tests.Pad.Timeline;

public sealed class TimelineStateTests
{
    [Fact]
    public void Append_assigns_serial_ids_and_preserves_structured_body()
    {
        var state = TimelineState
            .Empty.Append("dump", new Text("first"))
            .Append("console", new RawHtml("<code>second</code>"));

        Assert.Equal(2, state.Count);
        Assert.Equal(0, state[0].Id);
        Assert.Equal("dump", state[0].Reason);
        Assert.Equal(new Text("first"), state[0].Body);
        Assert.Equal(1, state[1].Id);
        Assert.Equal("console", state[1].Reason);
        Assert.Equal(new RawHtml("<code>second</code>"), state[1].Body);
        Assert.Equal(2, state.NextId);
    }

    [Fact]
    public void Append_does_not_mutate_original_state()
    {
        var original = TimelineState.Empty;
        var next = original.Append("dump", new Text("value"));

        Assert.Empty(original);
        Assert.Single(next);
    }

    [Fact]
    public void Replace_updates_one_existing_entry()
    {
        var state = TimelineState
            .Empty.Append("dump", new Text("first"))
            .Append("dump", new Text("second"));

        var next = state.Replace(new TimelineEntry(0, "render-error", new Text("failed")));

        Assert.Equal(new Text("failed"), next[0].Body);
        Assert.Equal("render-error", next[0].Reason);
        Assert.Equal(new Text("second"), next[1].Body);
        Assert.Equal(2, next.NextId);
    }

    [Fact]
    public void Replace_rejects_unknown_entry_id()
    {
        var state = TimelineState.Empty.Append("dump", new Text("value"));

        Assert.Throws<KeyNotFoundException>(() =>
            state.Replace(new TimelineEntry(99, "dump", new Text("missing")))
        );
    }

    [Fact]
    public void Clear_returns_empty_state()
    {
        var state = TimelineState.Empty.Append("dump", new Text("value"));

        Assert.Equal(TimelineState.Empty, state.Clear());
    }
}
