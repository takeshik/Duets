using Duets.Pad.Rendering;
using Duets.Pad.State;

namespace Duets.Pad.Tests.State;

public sealed class CanvasStateTests
{
    [Fact]
    public void Empty_has_a_stable_structured_root()
    {
        var state = CanvasState.Empty;

        Assert.Equal("div", state.Root.Tag);
        Assert.True(state.Root.Attributes.ContainsKey("data-duetspad-root"));
        Assert.Null(state.Root.Attributes["data-duetspad-root"]);
        Assert.Empty(state.Root.Children);
    }

    [Fact]
    public void Append_returns_a_new_state_with_the_node_added_to_root_children()
    {
        var original = CanvasState.Empty;
        var next = original.Append(new Text("hello"));

        Assert.Empty(original.Root.Children);
        Assert.Single(next.Root.Children);
        Assert.Equal(new Text("hello"), next.Root.Children[0]);
    }

    [Fact]
    public void Append_rejects_null_node()
    {
        var state = CanvasState.Empty;

        Assert.Throws<ArgumentNullException>(() => state.Append(null!));
    }

    [Fact]
    public void Set_returns_a_new_state_with_replaced_root_children()
    {
        var original = CanvasState.Empty.Append(new Text("before"));
        var next = original.Set(new ElementChildren(new Text("after")));

        Assert.Equal(new Text("before"), original.Root.Children[0]);
        Assert.Single(next.Root.Children);
        Assert.Equal(new Text("after"), next.Root.Children[0]);
    }

    [Fact]
    public void Clear_returns_empty_state()
    {
        var state = CanvasState.Empty.Append(new Text("hello"));

        Assert.Equal(CanvasState.Empty, state.Clear());
    }
}
