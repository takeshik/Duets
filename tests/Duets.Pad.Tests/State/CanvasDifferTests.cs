using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Duets.Pad.State;

namespace Duets.Pad.Tests.State;

public sealed class CanvasDifferTests
{
    private readonly CanvasDiffer differ = new();

    [Fact]
    public void Diff_returns_empty_for_equal_states()
    {
        var state = CanvasState.Empty.Append(new Text("hello"));

        var operations = this.differ.Diff(state, state);

        Assert.Empty(operations);
    }

    [Fact]
    public void Diff_emits_insert_child_for_tail_append()
    {
        var oldState = CanvasState.Empty.Append(new Text("first"));
        var newState = oldState.Append(new Text("second"));

        var operation = Assert.IsType<InsertChildOperation>(
            Assert.Single(this.differ.Diff(oldState, newState))
        );

        Assert.Equal([], operation.ParentPath.Segments);
        Assert.Equal(1, operation.Index);
        Assert.Equal(new Text("second"), operation.Node);
    }

    [Fact]
    public void Diff_emits_replace_text_for_text_value_change()
    {
        var oldState = CanvasState.Empty.Append(new Text("old"));
        var newState = CanvasState.Empty.Append(new Text("new"));

        var operation = Assert.IsType<ReplaceTextOperation>(
            Assert.Single(this.differ.Diff(oldState, newState))
        );

        Assert.Equal([0], operation.Path.Segments);
        Assert.Equal("new", operation.Value);
    }

    [Fact]
    public void Diff_emits_attribute_operations_for_element_attribute_changes()
    {
        var oldState = CanvasState.Empty.Append(
            new Element(
                "button",
                new ElementAttributes(
                    new KeyValuePair<string, string?>("class", "old"),
                    new KeyValuePair<string, string?>("disabled", null)
                )
            )
        );
        var newState = CanvasState.Empty.Append(
            new Element(
                "button",
                new ElementAttributes(
                    new KeyValuePair<string, string?>("class", "new"),
                    new KeyValuePair<string, string?>("title", "go")
                )
            )
        );

        var operations = this.differ.Diff(oldState, newState);

        Assert.Contains(
            operations,
            op =>
                op is SetAttributeOperation set
                && set.Path.Equals(new DisplayPath([0]))
                && set.Name == "class"
                && set.Value == "new"
        );
        Assert.Contains(
            operations,
            op =>
                op is SetAttributeOperation set
                && set.Path.Equals(new DisplayPath([0]))
                && set.Name == "title"
                && set.Value == "go"
        );
        Assert.Contains(
            operations,
            op =>
                op is RemoveAttributeOperation remove
                && remove.Path.Equals(new DisplayPath([0]))
                && remove.Name == "disabled"
        );
    }

    [Fact]
    public void Diff_emits_replace_node_for_raw_html_change()
    {
        var oldState = CanvasState.Empty.Append(new RawHtml("<b>old</b>"));
        var newState = CanvasState.Empty.Append(new RawHtml("<b>new</b>"));

        var operation = Assert.IsType<ReplaceNodeOperation>(
            Assert.Single(this.differ.Diff(oldState, newState))
        );

        Assert.Equal([0], operation.Path.Segments);
        Assert.Equal(new RawHtml("<b>new</b>"), operation.Node);
    }

    [Fact]
    public void Diff_emits_remove_child_for_tail_remove()
    {
        var oldState = CanvasState.Empty.Append(new Text("first")).Append(new Text("second"));
        var newState = CanvasState.Empty.Append(new Text("first"));

        var operation = Assert.IsType<RemoveChildOperation>(
            Assert.Single(this.differ.Diff(oldState, newState))
        );

        Assert.Equal([], operation.ParentPath.Segments);
        Assert.Equal(1, operation.Index);
    }
}
