using System.Text.Json.Nodes;
using Duets.Pad.Rendering;
using Duets.Pad.State;

namespace Duets.Tests.Pad.State;

public sealed class CanvasSerializerTests
{
    private readonly CanvasSerializer serializer = new();

    [Fact]
    public void CanvasState_Empty_serializes_to_root_element_with_data_duetspad_root()
    {
        var json = this.serializer.Serialize(CanvasState.Empty);

        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("div", (string?)json["tag"]);

        var attributes = Assert.IsType<JsonObject>(json["attributes"]);
        Assert.True(attributes.ContainsKey("data-duetspad-root"));
        Assert.Null(attributes["data-duetspad-root"]);

        var children = Assert.IsType<JsonArray>(json["children"]);
        Assert.Empty(children);
    }

    [Fact]
    public void Text_node_serializes_to_kind_text()
    {
        var state = CanvasState.Empty.Append(new Text("hello"));

        var json = this.serializer.Serialize(state);

        var children = Assert.IsType<JsonArray>(json["children"]);
        Assert.Single(children);

        var textNode = Assert.IsType<JsonObject>(children[0]);
        Assert.Equal("text", (string?)textNode["kind"]);
        Assert.Equal("hello", (string?)textNode["value"]);
    }

    [Fact]
    public void RawHtml_node_serializes_to_kind_rawHtml()
    {
        var state = CanvasState.Empty.Append(new RawHtml("<b>bold</b>"));

        var json = this.serializer.Serialize(state);

        var children = Assert.IsType<JsonArray>(json["children"]);
        var rawNode = Assert.IsType<JsonObject>(children[0]);
        Assert.Equal("rawHtml", (string?)rawNode["kind"]);
        Assert.Equal("<b>bold</b>", (string?)rawNode["content"]);
    }

    [Fact]
    public void Element_with_nested_children_serializes_recursively()
    {
        var inner = new Element(
            "span",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "highlight")),
            new ElementChildren(new Text("nested"))
        );
        var state = CanvasState.Empty.Append(inner);

        var json = this.serializer.Serialize(state);

        var rootChildren = Assert.IsType<JsonArray>(json["children"]);
        var spanNode = Assert.IsType<JsonObject>(rootChildren[0]);
        Assert.Equal("element", (string?)spanNode["kind"]);
        Assert.Equal("span", (string?)spanNode["tag"]);

        var spanAttrs = Assert.IsType<JsonObject>(spanNode["attributes"]);
        Assert.Equal("highlight", (string?)spanAttrs["class"]);

        var spanChildren = Assert.IsType<JsonArray>(spanNode["children"]);
        Assert.Single(spanChildren);
        var textNode = Assert.IsType<JsonObject>(spanChildren[0]);
        Assert.Equal("text", (string?)textNode["kind"]);
        Assert.Equal("nested", (string?)textNode["value"]);
    }

    [Fact]
    public void Null_attribute_value_is_preserved_as_json_null()
    {
        var element = new Element(
            "div",
            new ElementAttributes(
                new KeyValuePair<string, string?>("hidden", null),
                new KeyValuePair<string, string?>("class", "visible")
            )
        );
        var state = CanvasState.Empty.Append(element);

        var json = this.serializer.Serialize(state);

        var children = Assert.IsType<JsonArray>(json["children"]);
        var divNode = Assert.IsType<JsonObject>(children[0]);
        var attrs = Assert.IsType<JsonObject>(divNode["attributes"]);

        Assert.True(attrs.ContainsKey("hidden"));
        Assert.Null(attrs["hidden"]);
        Assert.Equal("visible", (string?)attrs["class"]);
    }

    [Fact]
    public void Unsupported_terminal_node_kind_throws()
    {
        // Use a non-terminal render node that passes through the pipeline's reducer
        // by bypassing CanvasState and calling RenderNodeJsonSerializer directly.
        // We define a test-only ITerminalRenderNode with CanReduce=false.
        var unsupported = new UnsupportedTerminalNode();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RenderNodeJsonSerializer.Serialize(unsupported)
        );

        Assert.Contains("not supported", exception.Message);
    }

    /// <summary>
    /// A test-only terminal render node kind that is not Text, Element, or RawHtml.
    /// </summary>
    private sealed class UnsupportedTerminalNode : ITerminalRenderNode
    {
        public bool CanReduce => false;

        public IRenderNode Reduce() => this;
    }
}
