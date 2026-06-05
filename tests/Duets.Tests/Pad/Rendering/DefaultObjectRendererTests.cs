using Duets.Pad.Rendering;

namespace Duets.Tests.Pad.Rendering;

public sealed class DefaultObjectRendererTests
{
    private readonly DefaultObjectRenderer renderer = new();

    [Fact]
    public void CanRender_always_returns_true()
    {
        Assert.True(this.renderer.CanRender("anything"));
        Assert.True(this.renderer.CanRender(42));
        Assert.True(this.renderer.CanRender(new object()));
    }

    [Fact]
    public void Render_string_returns_text_node()
    {
        var result = this.renderer.Render("hello world");

        Assert.Equal(new Text("hello world"), result);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Render_bool_returns_invariant_text(bool value, string expected)
    {
        var result = this.renderer.Render(value);

        Assert.Equal(new Text(expected), result);
    }

    [Theory]
    [InlineData((byte)255, "255")]
    [InlineData((sbyte)-1, "-1")]
    [InlineData((short)-32768, "-32768")]
    [InlineData((ushort)65535, "65535")]
    [InlineData(42, "42")]
    [InlineData((uint)4294967295u, "4294967295")]
    [InlineData((long)-9223372036854775808L, "-9223372036854775808")]
    [InlineData((ulong)18446744073709551615UL, "18446744073709551615")]
    [InlineData(3.14f, "3.14")]
    [InlineData(2.718281828d, "2.718281828")]
    public void Render_numeric_returns_invariant_culture_text(object value, string expected)
    {
        var result = this.renderer.Render(value);

        Assert.Equal(new Text(expected), result);
    }

    [Fact]
    public void Render_decimal_returns_invariant_culture_text()
    {
        var result = this.renderer.Render(1234567.89m);

        Assert.Equal(new Text("1234567.89"), result);
    }

    [Fact]
    public void Render_char_returns_text()
    {
        var result = this.renderer.Render('A');

        Assert.Equal(new Text("A"), result);
    }

    [Fact]
    public void Render_array_returns_duetspad_array_element()
    {
        var result = this.renderer.Render(new object[] { "a", 1, true });

        var element = Assert.IsType<Element>(result);
        Assert.Equal("div", element.Tag);
        Assert.Equal("duetspad-array", element.Attributes["class"]);
        Assert.Equal(3, element.Children.Count);
        Assert.Equal(new Text("a"), element.Children[0]);
        Assert.Equal(new Text("1"), element.Children[1]);
        Assert.Equal(new Text("true"), element.Children[2]);
    }

    [Fact]
    public void Render_dictionary_returns_duetspad_object_element()
    {
        var dict = new Dictionary<string, object?> { ["key1"] = "value1", ["key2"] = 42 };

        var result = this.renderer.Render(dict);

        var element = Assert.IsType<Element>(result);
        Assert.Equal("div", element.Tag);
        Assert.Equal("duetspad-object", element.Attributes["class"]);
        Assert.Equal(2, element.Children.Count);

        var entry0 = Assert.IsType<Element>(element.Children[0]);
        Assert.Equal("div", entry0.Tag);
        Assert.Equal(new Text("key1"), entry0.Children[0]);
        Assert.Equal(new Text("value1"), entry0.Children[1]);

        var entry1 = Assert.IsType<Element>(element.Children[1]);
        Assert.Equal("div", entry1.Tag);
        Assert.Equal(new Text("key2"), entry1.Children[0]);
        Assert.Equal(new Text("42"), entry1.Children[1]);
    }

    [Fact]
    public void Render_cyclic_graph_does_not_crash_and_yields_circular_marker()
    {
        var list = new List<object?>();
        list.Add(list); // self-referential

        var result = this.renderer.Render(list);

        var element = Assert.IsType<Element>(result);
        Assert.Equal("duetspad-array", element.Attributes["class"]);
        Assert.Single(element.Children);
        Assert.Equal(new Text("[Circular]"), element.Children[0]);
    }

    [Fact]
    public void Render_deep_nesting_hits_depth_marker()
    {
        // Build a list nested 40 levels deep — exceeds the default limit of 32.
        var innermost = new List<object?>();
        var current = innermost;
        for (var i = 0; i < 40; i++)
        {
            var next = new List<object?>();
            current.Add(next);
            current = next;
        }

        var result = this.renderer.Render(innermost);

        // Walk the nested structure and verify we eventually hit a […] marker.
        var node = result;
        var foundDepthMarker = false;

        for (var depth = 0; depth < 50; depth++)
        {
            if (node is not Element el)
            {
                break;
            }

            if (el.Children.Count == 0)
            {
                break;
            }

            var child = el.Children[0];

            if (child is Text { Value: "[…]" })
            {
                foundDepthMarker = true;
                break;
            }

            node = child;
        }

        Assert.True(
            foundDepthMarker,
            "Expected to find a '[…]' depth marker in the rendered output."
        );
    }

    [Fact]
    public void Render_unknown_object_uses_to_string()
    {
        var obj = new CustomToString("custom-value");

        var result = this.renderer.Render(obj);

        Assert.Equal(new Text("custom-value"), result);
    }

    private sealed class CustomToString(string value)
    {
        public override string ToString() => value;
    }
}
