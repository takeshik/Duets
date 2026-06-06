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

    // ── CLR object: property and field projection ─────────────────────────

    [Fact]
    public void Render_clr_object_with_property_and_field_returns_duetspad_object()
    {
        var obj = new SimpleRecord(Name: "Alice", Age: 30);

        var result = this.renderer.Render(obj);

        var element = Assert.IsType<Element>(result);
        Assert.Equal("div", element.Tag);
        Assert.Equal("duetspad-object", element.Attributes["class"]);
        // Expect Name and Age entries
        Assert.True(element.Children.Count >= 2);

        var entry0 = Assert.IsType<Element>(element.Children[0]);
        Assert.Equal("div", entry0.Tag);
        Assert.Equal(new Text("Name"), entry0.Children[0]);
        Assert.Equal(new Text("Alice"), entry0.Children[1]);

        var entry1 = Assert.IsType<Element>(element.Children[1]);
        Assert.Equal("div", entry1.Tag);
        Assert.Equal(new Text("Age"), entry1.Children[0]);
        Assert.Equal(new Text("30"), entry1.Children[1]);
    }

    [Fact]
    public void Render_clr_object_with_public_field_includes_field_in_output()
    {
        var obj = new ObjectWithPublicField { Tag = "hello", Count = 5 };

        var result = this.renderer.Render(obj);

        var element = Assert.IsType<Element>(result);
        Assert.Equal("duetspad-object", element.Attributes["class"]);

        var entries = element.Children.OfType<Element>().ToList();
        Assert.Contains(entries, e => e.Children[0] is Text { Value: "Tag" });
        Assert.Contains(entries, e => e.Children[0] is Text { Value: "Count" });
    }

    // ── CLR object: list renders as table ─────────────────────────────────

    [Fact]
    public void Render_object_list_returns_duetspad_table()
    {
        var list = new[] { new SimpleRecord("Alice", 30), new SimpleRecord("Bob", 25) };

        var result = this.renderer.Render(list);

        var element = Assert.IsType<Element>(result);
        Assert.Equal("table", element.Tag);
        Assert.Equal("duetspad-table", element.Attributes["class"]);

        // thead > tr > th*
        var thead = Assert.IsType<Element>(element.Children[0]);
        Assert.Equal("thead", thead.Tag);
        var headerRow = Assert.IsType<Element>(thead.Children[0]);
        Assert.Equal("tr", headerRow.Tag);
        var headers = headerRow.Children.OfType<Element>().ToList();
        Assert.Equal(2, headers.Count);
        Assert.Equal("Name", Assert.IsType<Text>(headers[0].Children[0]).Value);
        Assert.Equal("Age", Assert.IsType<Text>(headers[1].Children[0]).Value);

        // tbody > tr * 2
        var tbody = Assert.IsType<Element>(element.Children[1]);
        Assert.Equal("tbody", tbody.Tag);
        Assert.Equal(2, tbody.Children.Count);

        var row0 = Assert.IsType<Element>(tbody.Children[0]);
        var cells0 = row0.Children.OfType<Element>().ToList();
        Assert.Equal("Alice", Assert.IsType<Text>(cells0[0].Children[0]).Value);
        Assert.Equal("30", Assert.IsType<Text>(cells0[1].Children[0]).Value);
    }

    [Fact]
    public void Render_dictionary_list_returns_duetspad_table()
    {
        var list = new List<Dictionary<string, object?>>
        {
            new() { ["x"] = 1, ["y"] = 2 },
            new() { ["x"] = 3, ["y"] = 4 },
        };

        var result = this.renderer.Render(list);

        var element = Assert.IsType<Element>(result);
        Assert.Equal("table", element.Tag);
        Assert.Equal("duetspad-table", element.Attributes["class"]);

        var thead = Assert.IsType<Element>(element.Children[0]);
        var headerRow = Assert.IsType<Element>(thead.Children[0]);
        var headers = headerRow.Children.OfType<Element>().ToList();
        Assert.Equal(2, headers.Count);
        Assert.Equal("x", Assert.IsType<Text>(headers[0].Children[0]).Value);
        Assert.Equal("y", Assert.IsType<Text>(headers[1].Children[0]).Value);
    }

    // ── Tabular: zero-column fallback ────────────────────────────────────

    [Fact]
    public void Render_list_of_no_member_objects_falls_back_to_array_not_empty_table()
    {
        // CustomToString has no public properties or fields — projects to zero members.
        var list = new object[] { new CustomToString("x"), new CustomToString("y") };

        var result = this.renderer.Render(list);

        // Must NOT be a table — falls back to duetspad-array.
        var element = Assert.IsType<Element>(result);
        Assert.Equal("div", element.Tag);
        Assert.Equal("duetspad-array", element.Attributes["class"]);
        Assert.Equal(2, element.Children.Count);
        Assert.Equal(new Text("x"), element.Children[0]);
        Assert.Equal(new Text("y"), element.Children[1]);
    }

    [Fact]
    public void Render_object_list_with_real_members_still_returns_table()
    {
        // Sanity: the zero-column fix must not regress normal record-list tabular rendering.
        var list = new[] { new SimpleRecord("Alice", 30), new SimpleRecord("Bob", 25) };

        var result = this.renderer.Render(list);

        var element = Assert.IsType<Element>(result);
        Assert.Equal("table", element.Tag);
        Assert.Equal("duetspad-table", element.Attributes["class"]);
    }

    // ── Tabular: union of columns across heterogeneous rows ───────────────

    [Fact]
    public void Render_heterogeneous_record_list_uses_union_of_columns()
    {
        var list = new object[]
        {
            new Dictionary<string, object?> { ["a"] = 1 },
            new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 },
        };

        var result = this.renderer.Render(list);

        var element = Assert.IsType<Element>(result);
        Assert.Equal("table", element.Tag);

        var thead = Assert.IsType<Element>(element.Children[0]);
        var headerRow = Assert.IsType<Element>(thead.Children[0]);
        var headers = headerRow
            .Children.OfType<Element>()
            .Select(h => Assert.IsType<Text>(h.Children[0]).Value)
            .ToList();

        // "a" appears first (from first row), "b" appears second (from second row)
        Assert.Equal(["a", "b"], headers);

        // Row 0 cell for "b" should be empty (missing in that row)
        var tbody = Assert.IsType<Element>(element.Children[1]);
        var row0 = Assert.IsType<Element>(tbody.Children[0]);
        var cells0 = row0.Children.OfType<Element>().ToList();
        Assert.Equal("", Assert.IsType<Text>(cells0[1].Children[0]).Value);
    }

    // ── CLR object: getter exception → [error] marker ────────────────────

    [Fact]
    public void Render_object_with_throwing_getter_includes_error_marker_not_exception()
    {
        var obj = new ObjectWithThrowingGetter();

        // Must not throw; the throwing property should appear as an error marker.
        var result = this.renderer.Render(obj);

        var element = Assert.IsType<Element>(result);
        Assert.Equal("duetspad-object", element.Attributes["class"]);

        var entries = element.Children.OfType<Element>().ToList();
        var errorEntry = entries.FirstOrDefault(e => e.Children[0] is Text { Value: "Exploding" });
        Assert.NotNull(errorEntry);
        Assert.Equal(new Text("[error]"), errorEntry.Children[1]);
    }

    // ── Render node passthrough ───────────────────────────────────────────

    [Fact]
    public void Render_array_containing_text_node_passes_through_the_node()
    {
        var textNode = new Text("hello");
        var result = this.renderer.Render(new object[] { textNode });

        var element = Assert.IsType<Element>(result);
        Assert.Equal("duetspad-array", element.Attributes["class"]);
        Assert.Single(element.Children);
        Assert.Same(textNode, element.Children[0]);
    }

    [Fact]
    public void Render_array_containing_element_node_passes_through_the_node()
    {
        var label = new Element(
            "span",
            new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-label")),
            new ElementChildren(new Text("x"))
        );
        var result = this.renderer.Render(new object[] { label });

        var element = Assert.IsType<Element>(result);
        Assert.Equal("duetspad-array", element.Attributes["class"]);
        Assert.Single(element.Children);
        Assert.Same(label, element.Children[0]);
    }

    [Fact]
    public void Render_object_property_returning_render_node_is_passed_through()
    {
        var obj = new ObjectWithRenderNodeProperty();

        var result = this.renderer.Render(obj);

        var element = Assert.IsType<Element>(result);
        Assert.Equal("duetspad-object", element.Attributes["class"]);

        var entry = Assert.IsType<Element>(element.Children[0]);
        Assert.Equal(new Text("Node"), entry.Children[0]);
        // The value should be the Element itself, not a reflected Text object.
        var child = Assert.IsType<Element>(entry.Children[1]);
        Assert.Equal("span", child.Tag);
        Assert.Equal("duetspad-label", child.Attributes["class"]);
    }

    [Fact]
    public void Render_object_list_getter_exception_produces_text_error_marker_in_table_cell()
    {
        var list = new[] { new ObjectWithThrowingGetter() };

        var result = this.renderer.Render(list);

        // List of record-like objects → tabular rendering.
        var table = Assert.IsType<Element>(result);
        Assert.Equal("table", table.Tag);
        Assert.Equal("duetspad-table", table.Attributes["class"]);

        var tbody = Assert.IsType<Element>(table.Children[1]);
        var row = Assert.IsType<Element>(tbody.Children[0]);
        var cells = row.Children.OfType<Element>().ToList();

        // Find the "Exploding" column cell.
        var thead = Assert.IsType<Element>(table.Children[0]);
        var headerRow = Assert.IsType<Element>(thead.Children[0]);
        var headers = headerRow.Children.OfType<Element>().ToList();
        var explodingIndex = headers.FindIndex(h => h.Children[0] is Text { Value: "Exploding" });
        Assert.True(explodingIndex >= 0, "Expected 'Exploding' column in table headers.");

        // The cell content should be the [error] Text marker.
        var errorCell = cells[explodingIndex];
        Assert.Equal(new Text("[error]"), errorCell.Children[0]);
    }

    // ── Helper types ──────────────────────────────────────────────────────

    private sealed record SimpleRecord(string Name, int Age);

    private sealed class ObjectWithPublicField
    {
        public string Tag { get; set; } = "";

        public int Count;
    }

    private sealed class ObjectWithThrowingGetter
    {
        public string Safe => "ok";

        public string Exploding => throw new InvalidOperationException("boom");
    }

    private sealed class CustomToString(string value)
    {
        public override string ToString() => value;
    }

    private sealed class ObjectWithRenderNodeProperty
    {
        public IRenderNode Node { get; } =
            new Element(
                "span",
                new ElementAttributes(new KeyValuePair<string, string?>("class", "duetspad-label")),
                new ElementChildren(new Text("rendered"))
            );
    }
}
