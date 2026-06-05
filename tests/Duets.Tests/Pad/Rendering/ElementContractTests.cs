using Duets.Pad.Rendering;

namespace Duets.Tests.Pad.Rendering;

public sealed class ElementContractTests
{
    [Fact]
    public void Element_normalizes_tag_and_uses_structural_equality()
    {
        var left = new Element(
            "DIV",
            new ElementAttributes(
                new KeyValuePair<string, string?>("data-kind", "metric"),
                new KeyValuePair<string, string?>("CLASS", "card")
            ),
            new ElementChildren(new Text("CPU"), new RawHtml("<strong>42%</strong>"))
        );
        var right = new Element(
            "div",
            new ElementAttributes(
                new KeyValuePair<string, string?>("class", "card"),
                new KeyValuePair<string, string?>("DATA-KIND", "metric")
            ),
            new ElementChildren(new Text("CPU"), new RawHtml("<strong>42%</strong>"))
        );

        Assert.Equal("div", left.Tag);
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void ElementChildren_are_order_sensitive_terminal_node_lists()
    {
        var left = new ElementChildren(new Text("a"), new Text("b"));
        var right = new ElementChildren(new Text("b"), new Text("a"));

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void ElementChildren_take_an_immutable_snapshot()
    {
        var source = new List<ITerminalRenderNode> { new Text("before") };
        var children = new ElementChildren(source);

        source[0] = new Text("after");

        Assert.Equal(new Text("before"), children[0]);
    }

    [Fact]
    public void ElementAttributes_are_a_name_keyed_map_with_deterministic_enumeration()
    {
        var attributes = new ElementAttributes(
            new KeyValuePair<string, string?>("data-kind", "metric"),
            new KeyValuePair<string, string?>("CLASS", "card"),
            new KeyValuePair<string, string?>("id", "cpu")
        );

        Assert.Equal(3, attributes.Count);
        Assert.True(attributes.ContainsKey("class"));
        Assert.True(attributes.ContainsKey("CLASS"));
        Assert.Equal("card", attributes["class"]);
        Assert.Equal("metric", attributes["DATA-KIND"]);
        Assert.Equal(
            ["class", "data-kind", "id"],
            [.. attributes.Select(attribute => attribute.Key)]
        );
    }

    [Fact]
    public void ElementAttributes_reject_duplicate_names_after_normalization()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ElementAttributes(
                new KeyValuePair<string, string?>("class", "card"),
                new KeyValuePair<string, string?>("CLASS", "btn")
            )
        );

        Assert.Contains("is specified more than once", exception.Message);
    }

    [Fact]
    public void ElementAttributes_reject_event_attributes()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ElementAttributes(new KeyValuePair<string, string?>("onclick", "alert(1)"))
        );

        Assert.Contains(
            "event attributes require a separate interaction contract",
            exception.Message
        );
    }

    [Fact]
    public void ElementAttributes_preserve_null_and_string_values()
    {
        var attributes = new ElementAttributes(
            new KeyValuePair<string, string?>("hidden", null),
            new KeyValuePair<string, string?>("data-kind", "metric"),
            new KeyValuePair<string, string?>("style", "display: block")
        );

        Assert.True(attributes.TryGetValue("hidden", out var hidden));
        Assert.Null(hidden);
        Assert.Equal("metric", attributes["data-kind"]);
        Assert.Equal("display: block", attributes["style"]);
    }

    [Theory]
    [InlineData("href")]
    [InlineData("src")]
    [InlineData("action")]
    [InlineData("formaction")]
    [InlineData("poster")]
    [InlineData("srcset")]
    public void ElementAttributes_reject_javascript_url_scheme(string attributeName)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ElementAttributes(
                new KeyValuePair<string, string?>(attributeName, "javascript:alert(1)")
            )
        );

        Assert.Contains("javascript: URL scheme", exception.Message);
    }

    [Fact]
    public void ElementAttributes_reject_srcdoc()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ElementAttributes(new KeyValuePair<string, string?>("srcdoc", "<p>html</p>"))
        );

        Assert.Contains("inline HTML payloads must use RawHtml explicitly", exception.Message);
    }

    [Fact]
    public void Element_rejects_disallowed_tags()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Element("script"));

        Assert.Contains("is not allowed", exception.Message);
    }

    [Theory]
    [InlineData("button")]
    [InlineData("input")]
    [InlineData("x-debug-panel")]
    [InlineData("future-html-element")]
    public void Element_allows_known_and_unknown_structured_tags(string tag)
    {
        var element = new Element(tag);

        Assert.Equal(tag, element.Tag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad tag")]
    [InlineData("bad<tag")]
    [InlineData("bad/tag")]
    public void Element_rejects_invalid_tag_name_syntax(string tag)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Element(tag));

        Assert.Contains("Element tag", exception.Message);
    }

    [Fact]
    public void ElementChildren_collection_expression_with_literals_builds_correctly()
    {
        ElementChildren xs = [new Text("a"), new Text("b")];

        Assert.Equal(2, xs.Count);
        Assert.Equal(new Text("a"), xs[0]);
        Assert.Equal(new Text("b"), xs[1]);
    }

    [Fact]
    public void ElementChildren_collection_expression_with_spread_builds_correctly()
    {
        ITerminalRenderNode[] source = [new Text("a"), new Text("b")];

        ElementChildren xs = [.. source];

        Assert.Equal(2, xs.Count);
        Assert.Equal(new Text("a"), xs[0]);
        Assert.Equal(new Text("b"), xs[1]);
    }

    [Fact]
    public void ElementChildren_collection_expression_rejects_null_element()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            ElementChildren _ = [new Text("a"), null!];
        });
    }
}
