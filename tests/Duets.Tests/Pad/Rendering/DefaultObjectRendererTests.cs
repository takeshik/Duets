using Duets.Pad.Rendering;
using Duets.Tests.TestSupport;

namespace Duets.Tests.Pad.Rendering;

public sealed class DefaultObjectRendererTests
{
    private readonly DefaultObjectRenderer renderer = new();

    /// <summary>
    /// Asserts that <paramref name="node"/> is a member row <c>&lt;tr&gt;</c> of a named-member
    /// object table (Form A) — a <c>&lt;th class="duetspad-key"&gt;</c> holding a <see cref="Text"/>
    /// with the given member name, followed by a <c>&lt;td&gt;</c> holding the value — and returns
    /// the row for further inspection.
    /// </summary>
    private static Element AssertMemberRow(ITerminalRenderNode node, string expectedKey)
    {
        var row = Assert.IsType<Element>(node);
        Assert.Equal("tr", row.Tag);

        var keyElement = Assert.IsType<Element>(row.Children[0]);
        Assert.Equal("th", keyElement.Tag);
        Assert.Equal("duetspad-key", keyElement.Attributes["class"]);
        Assert.Equal(new Text(expectedKey), Assert.Single(keyElement.Children));

        Assert.IsType<Element>(row.Children[1]);
        Assert.Equal("td", ((Element)row.Children[1]).Tag);

        return row;
    }

    /// <summary>
    /// Returns the value node held by the <c>&lt;td&gt;</c> of a member row produced by
    /// <see cref="AssertMemberRow"/>.
    /// </summary>
    private static ITerminalRenderNode GetRowValue(Element row) =>
        Assert.Single(((Element)row.Children[1]).Children);

    /// <summary>
    /// Asserts that <paramref name="result"/> is a Form A named-member object table
    /// (<c>&lt;table class="duetspad-object"&gt;</c>) and returns its <c>&lt;tbody&gt;</c> rows.
    /// </summary>
    private static (Element Table, IReadOnlyList<Element> Rows, Element? Thead) AssertObjectTable(
        IRenderNode result
    )
    {
        var table = Assert.IsType<Element>(result);
        Assert.Equal("table", table.Tag);
        Assert.Equal("duetspad-object", table.Attributes["class"]);

        Element? thead = null;
        Element tbody;

        if (table.Children.Count == 2)
        {
            thead = Assert.IsType<Element>(table.Children[0]);
            Assert.Equal("thead", thead.Tag);
            tbody = Assert.IsType<Element>(table.Children[1]);
        }
        else
        {
            tbody = Assert.IsType<Element>(table.Children[0]);
        }

        Assert.Equal("tbody", tbody.Tag);

        var rows = tbody.Children.Select(Assert.IsType<Element>).ToList();

        return (table, rows, thead);
    }

    /// <summary>
    /// Asserts that <paramref name="thead"/> is a typeheader row holding the given type name in
    /// a <c>&lt;th class="duetspad-typeheader" colspan="2"&gt;</c>.
    /// </summary>
    private static void AssertTypeHeader(Element thead, string expectedTypeName)
    {
        var headerRow = Assert.IsType<Element>(Assert.Single(thead.Children));
        Assert.Equal("tr", headerRow.Tag);

        var th = Assert.IsType<Element>(Assert.Single(headerRow.Children));
        Assert.Equal("th", th.Tag);
        Assert.Equal("duetspad-typeheader", th.Attributes["class"]);
        Assert.Equal("2", th.Attributes["colspan"]);
        Assert.Equal(new Text(expectedTypeName), Assert.Single(th.Children));
    }

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
    public void Render_dictionary_returns_duetspad_map_table()
    {
        var dict = new Dictionary<string, object?> { ["key1"] = "value1", ["key2"] = 42 };

        var result = this.renderer.Render(dict);

        var table = Assert.IsType<Element>(result);
        Assert.Equal("table", table.Tag);
        Assert.Equal("duetspad-map", table.Attributes["class"]);

        var thead = Assert.IsType<Element>(table.Children[0]);
        Assert.Equal("thead", thead.Tag);
        Assert.Equal(2, thead.Children.Count);

        // Type header row: th.duetspad-typeheader[colspan=2] = "{TypeName} ({N} items)"
        var typeHeaderRow = Assert.IsType<Element>(thead.Children[0]);
        Assert.Equal("tr", typeHeaderRow.Tag);
        var typeHeaderCell = Assert.IsType<Element>(Assert.Single(typeHeaderRow.Children));
        Assert.Equal("th", typeHeaderCell.Tag);
        Assert.Equal("duetspad-typeheader", typeHeaderCell.Attributes["class"]);
        Assert.Equal("2", typeHeaderCell.Attributes["colspan"]);
        Assert.Equal(
            new Text($"{dict.GetType().Name} (2 items)"),
            Assert.Single(typeHeaderCell.Children)
        );

        // Key/Value column header row.
        var columnHeaderRow = Assert.IsType<Element>(thead.Children[1]);
        Assert.Equal("tr", columnHeaderRow.Tag);
        var columnHeaders = columnHeaderRow.Children.OfType<Element>().ToList();
        Assert.Equal(2, columnHeaders.Count);
        Assert.Equal(new Text("Key"), Assert.Single(columnHeaders[0].Children));
        Assert.Equal(new Text("Value"), Assert.Single(columnHeaders[1].Children));

        var tbody = Assert.IsType<Element>(table.Children[1]);
        Assert.Equal("tbody", tbody.Tag);
        Assert.Equal(2, tbody.Children.Count);

        var row0 = Assert.IsType<Element>(tbody.Children[0]);
        var cells0 = row0.Children.OfType<Element>().ToList();
        Assert.Equal("td", cells0[0].Tag);
        Assert.Equal(new Text("key1"), Assert.Single(cells0[0].Children));
        Assert.Equal(new Text("value1"), Assert.Single(cells0[1].Children));

        var row1 = Assert.IsType<Element>(tbody.Children[1]);
        var cells1 = row1.Children.OfType<Element>().ToList();
        Assert.Equal(new Text("key2"), Assert.Single(cells1[0].Children));
        Assert.Equal(new Text("42"), Assert.Single(cells1[1].Children));
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

        var (_, rows, thead) = AssertObjectTable(result);
        Assert.NotNull(thead);
        AssertTypeHeader(thead!, nameof(SimpleRecord));

        // Expect Name and Age entries
        Assert.True(rows.Count >= 2);

        var row0 = AssertMemberRow(rows[0], "Name");
        Assert.Equal(new Text("Alice"), GetRowValue(row0));

        var row1 = AssertMemberRow(rows[1], "Age");
        Assert.Equal(new Text("30"), GetRowValue(row1));
    }

    [Fact]
    public void Render_clr_object_with_terminal_text_members_keeps_key_and_value_distinct()
    {
        // Regression test: the member name must remain structurally distinct from its value —
        // the key is a <th> and the value lives in a separate <td>, so adjacent terminal Text
        // nodes cannot merge their rendered text.
        var obj = new SimpleRecord(Name: "Alice", Age: 30);

        var result = this.renderer.Render(obj);

        var (_, rows, _) = AssertObjectTable(result);
        var row = rows[0];

        var keyElement = Assert.IsType<Element>(row.Children[0]);
        Assert.Equal("th", keyElement.Tag);
        Assert.Equal("duetspad-key", keyElement.Attributes["class"]);

        var valueNode = GetRowValue(row);
        Assert.IsType<Text>(valueNode);
        Assert.NotSame(keyElement, valueNode);
    }

    [Fact]
    public void Render_clr_object_with_public_field_includes_field_in_output()
    {
        var obj = new ObjectWithPublicField { Tag = "hello", Count = 5 };

        var result = this.renderer.Render(obj);

        var (_, rows, thead) = AssertObjectTable(result);
        Assert.NotNull(thead);
        AssertTypeHeader(thead!, nameof(ObjectWithPublicField));

        Assert.Contains(
            rows,
            r =>
                r.Children[0] is Element { Tag: "th" } key
                && key.Children[0] is Text { Value: "Tag" }
        );
        Assert.Contains(
            rows,
            r =>
                r.Children[0] is Element { Tag: "th" } key
                && key.Children[0] is Text { Value: "Count" }
        );
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

        var (_, rows, _) = AssertObjectTable(result);

        var errorRow = rows.FirstOrDefault(r =>
            r.Children[0] is Element { Tag: "th" } key
            && key.Children[0] is Text { Value: "Exploding" }
        );
        Assert.NotNull(errorRow);
        Assert.Equal(new Text("[error]"), GetRowValue(errorRow!));
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

        var (_, rows, _) = AssertObjectTable(result);

        var row = AssertMemberRow(rows[0], "Node");
        // The value should be the Element itself, not a reflected Text object.
        var child = Assert.IsType<Element>(GetRowValue(row));
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

    // ── ADR-40: enum and JS-object/CLR-object convergence regressions ────

    [Fact]
    public void Render_enum_returns_text_with_member_name()
    {
        var result = this.renderer.Render(PlatformID.Win32NT);

        Assert.Equal(new Text("Win32NT"), result);
    }

    [Fact]
    public void Render_expando_object_converges_with_clr_object_presentation()
    {
        // ExpandoObject is the shape Jint marshals JS object literals to: a string-keyed,
        // enumerable, dictionary-like value that does NOT implement non-generic IDictionary.
        // ADR-40 routes it through the named-member-object presentation (Form A) — the same
        // <table class="duetspad-object"> shape as an ordinary CLR object — rather than the
        // Key/Value map grid (Form B), and OMITS the type header (the marshaled CLR type name
        // like "ExpandoObject" would be noise to a script author).
        dynamic expando = new System.Dynamic.ExpandoObject();
        expando.foo = "abc";
        expando.bar = 42;

        var result = this.renderer.Render((object)expando);

        var (_, rows, thead) = AssertObjectTable(result);
        Assert.Null(thead);

        var row0 = AssertMemberRow(rows[0], "foo");
        Assert.Equal(new Text("abc"), GetRowValue(row0));

        var row1 = AssertMemberRow(rows[1], "bar");
        Assert.Equal(new Text("42"), GetRowValue(row1));
    }

    [Fact]
    public void Render_empty_expando_object_returns_empty_object_table_without_type_leak()
    {
        // Regression: dump({}) marshals to an empty ExpandoObject. It must render as an empty
        // Form A object table (no rows), NOT fall back to value.ToString() and leak the marshaled
        // CLR type name "System.Dynamic.ExpandoObject".
        var expando = new System.Dynamic.ExpandoObject();

        var result = this.renderer.Render(expando);

        var (_, rows, thead) = AssertObjectTable(result);
        Assert.Null(thead);
        Assert.Empty(rows);
    }

    [Fact]
    public void Render_generic_only_read_only_dictionary_returns_map_table()
    {
        // Regression (Symptom 1): a genuine generic-only CLR map — implements
        // IReadOnlyDictionary<,> (hence IEnumerable<KeyValuePair<,>>) but NOT non-generic
        // IDictionary and is NOT a dynamic JS object — must render as a Key/Value map grid
        // (Form B, duetspad-map), not a named-member object table.
        var map = new GenericOnlyReadOnlyDictionary(
            new Dictionary<string, object?> { ["key1"] = "value1", ["key2"] = 42 }
        );

        var result = this.renderer.Render(map);

        var table = Assert.IsType<Element>(result);
        Assert.Equal("table", table.Tag);
        Assert.Equal("duetspad-map", table.Attributes["class"]);
    }

    [Fact]
    public void Render_real_jint_js_object_literal_uses_object_presentation()
    {
        // End-to-end: a JS object literal is marshaled by Jint to System.Dynamic.ExpandoObject.
        // It must render through the named-member object presentation (Form A,
        // duetspad-object), converging with ordinary CLR objects, not the Key/Value map grid.
        using var engine = JintTestRuntime.CreateEngine();
        var value = engine.Evaluate("({a:1})").ToObject();
        Assert.NotNull(value);

        var result = this.renderer.Render(value!);

        var (_, rows, thead) = AssertObjectTable(result);
        Assert.Null(thead);

        var row0 = AssertMemberRow(rows[0], "a");
        Assert.Equal(new Text("1"), GetRowValue(row0));
    }

    [Fact]
    public void Render_expando_object_collection_renders_as_single_table_not_array_of_objects()
    {
        // ADR-40 classifies a JS object literal (ExpandoObject) as a named-member object, the same
        // as a CLR object, in isolation and within collections. The concrete shape is not fixed by
        // the ADR; this test verifies that convergence at the implementation level: a collection of
        // such values renders as a single duetspad-table, not a duetspad-array of duetspad-objects.
        dynamic obj1 = new System.Dynamic.ExpandoObject();
        obj1.a = 1;
        dynamic obj2 = new System.Dynamic.ExpandoObject();
        obj2.a = 2;
        var list = new List<object> { (object)obj1, (object)obj2 };

        var result = this.renderer.Render(list);

        var table = Assert.IsType<Element>(result);
        Assert.Equal("table", table.Tag);
        Assert.Equal("duetspad-table", table.Attributes["class"]);

        // Verify the "a" column is present.
        var thead = Assert.IsType<Element>(table.Children[0]);
        var headerRow = Assert.IsType<Element>(thead.Children[0]);
        var headers = headerRow
            .Children.OfType<Element>()
            .Select(h => Assert.IsType<Text>(h.Children[0]).Value)
            .ToList();
        Assert.Contains("a", headers);
    }

    [Fact]
    public void Render_bare_key_value_pair_sequence_is_not_a_map()
    {
        // A List<KeyValuePair<,>> implements IEnumerable<KeyValuePair<,>> but NOT IDictionary<,>
        // or IReadOnlyDictionary<,>. It is intentionally NOT classified as a map (Form B) and
        // falls through to the collection path. Assert the result is NOT duetspad-map.
        var list = new List<KeyValuePair<string, object?>>
        {
            new("key1", "value1"),
            new("key2", 42),
        };

        var result = this.renderer.Render(list);

        var element = Assert.IsType<Element>(result);
        Assert.NotEqual("duetspad-map", element.Attributes["class"]);
    }

    [Fact]
    public void Render_object_with_nested_object_member_renders_nested_table()
    {
        var obj = new ObjectWithNestedMember(Inner: new SimpleRecord("Alice", 30));

        var result = this.renderer.Render(obj);

        var (_, rows, _) = AssertObjectTable(result);
        var row = AssertMemberRow(rows[0], "Inner");

        var (_, nestedRows, nestedThead) = AssertObjectTable(GetRowValue(row));
        Assert.NotNull(nestedThead);
        AssertTypeHeader(nestedThead!, nameof(SimpleRecord));

        var nestedRow0 = AssertMemberRow(nestedRows[0], "Name");
        Assert.Equal(new Text("Alice"), GetRowValue(nestedRow0));
    }

    // ── Helper types ──────────────────────────────────────────────────────

    private sealed record SimpleRecord(string Name, int Age);

    private sealed record ObjectWithNestedMember(SimpleRecord Inner);

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

    /// <summary>
    /// A generic-only map: implements <see cref="IReadOnlyDictionary{TKey, TValue}"/> (hence
    /// <see cref="IEnumerable{T}"/> of <see cref="KeyValuePair{TKey, TValue}"/>) but NOT the
    /// non-generic <see cref="System.Collections.IDictionary"/> and is NOT a dynamic JS object.
    /// Used to verify such values render as a map (Form B), not a named-member object table.
    /// </summary>
    private sealed class GenericOnlyReadOnlyDictionary(IReadOnlyDictionary<string, object?> inner)
        : IReadOnlyDictionary<string, object?>
    {
        public object? this[string key] => inner[key];

        public IEnumerable<string> Keys => inner.Keys;

        public IEnumerable<object?> Values => inner.Values;

        public int Count => inner.Count;

        public bool ContainsKey(string key) => inner.ContainsKey(key);

        public bool TryGetValue(string key, out object? value) => inner.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            inner.GetEnumerator();
    }
}
