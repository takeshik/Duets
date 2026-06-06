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

    // -------------------------------------------------------------------------
    // Trim
    // -------------------------------------------------------------------------

    [Fact]
    public void Trim_removes_entries_below_boundary_and_preserves_NextId()
    {
        var state = TimelineState
            .Empty.Append("dump", new Text("a")) // id 0
            .Append("dump", new Text("b")) // id 1
            .Append("dump", new Text("c")) // id 2
            .Append("dump", new Text("d")); // id 3

        // Trim below id 2: entries 0 and 1 should be gone.
        var trimmed = state.Trim(2);

        Assert.Equal(2, trimmed.Count);
        Assert.Equal(2, trimmed[0].Id);
        Assert.Equal(3, trimmed[1].Id);
        // NextId must be preserved (not reset).
        Assert.Equal(state.NextId, trimmed.NextId);
        Assert.Equal(4, trimmed.NextId);
    }

    [Fact]
    public void Trim_noop_when_boundary_is_at_or_below_lowest_id()
    {
        var state = TimelineState
            .Empty.Append("dump", new Text("a")) // id 0
            .Append("dump", new Text("b")); // id 1

        // Boundary at id 0 means "keep everything at id >= 0" — nothing removed.
        var result0 = state.Trim(0);
        Assert.Same(state, result0);

        // Boundary below all ids also removes nothing.
        var resultNeg = state.Trim(-5);
        Assert.Same(state, resultNeg);
    }

    [Fact]
    public void Trim_removes_all_entries_when_boundary_above_all_ids_and_preserves_nextid()
    {
        var state = TimelineState
            .Empty.Append("dump", new Text("x")) // id 0
            .Append("dump", new Text("y")); // id 1

        // removeBeforeId == NextId (2): no entry has id >= 2, so all are removed.
        var result = state.Trim(state.NextId);

        Assert.Empty(result);
        // NextId must be preserved so that a subsequent Append does not reuse id 0 or 1.
        Assert.Equal(state.NextId, result.NextId);
    }

    [Fact]
    public void Trim_preserves_NextId_so_ids_are_never_reused()
    {
        var state = TimelineState
            .Empty.Append("dump", new Text("a")) // id 0
            .Append("dump", new Text("b")) // id 1
            .Append("dump", new Text("c")); // id 2

        var trimmed = state.Trim(2); // keep only id 2
        Assert.Single(trimmed);
        Assert.Equal(3, trimmed.NextId);

        // A subsequent append uses id 3, not 0.
        var next = trimmed.Append("dump", new Text("d"));
        Assert.Equal(3, next[^1].Id);
    }
}
