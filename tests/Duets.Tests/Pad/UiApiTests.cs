using Duets.Pad;
using Duets.Pad.Rendering;

namespace Duets.Tests.Pad;

public sealed class UiApiTests
{
    private static UiApi CreateUiApi() => new(new ObjectRenderingPipeline([]));

    // ── Positive: RawHtml ──────────────────────────────────────────────────

    [Fact]
    public void RawHtml_returns_RawHtml_node()
    {
        var ui = CreateUiApi();

        var result = ui.RawHtml("<strong>hello</strong>");

        var node = Assert.IsType<RawHtml>(result);
        Assert.Equal("<strong>hello</strong>", node.Content);
    }

    // ── Positive: Text ────────────────────────────────────────────────────

    [Fact]
    public void Text_returns_Text_node()
    {
        var ui = CreateUiApi();

        var result = ui.Text("hello");

        var node = Assert.IsType<Duets.Pad.Rendering.Text>(result);
        Assert.Equal("hello", node.Value);
    }

    // ── Positive: Label ───────────────────────────────────────────────────

    [Fact]
    public void Label_returns_span_with_duetspad_label_class_and_text_child()
    {
        var ui = CreateUiApi();

        var result = ui.Label("my label");

        var json = RenderNodeJsonSerializer.Serialize(result);
        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("span", (string?)json["tag"]);
        Assert.Equal("duetspad-label", (string?)json["attributes"]!["class"]);
        Assert.Single(json["children"]!.AsArray());
        Assert.Equal("text", (string?)json["children"]![0]!["kind"]);
        Assert.Equal("my label", (string?)json["children"]![0]!["value"]);
    }

    // ── Positive: Stack ───────────────────────────────────────────────────

    [Fact]
    public void Stack_with_no_children_returns_empty_div()
    {
        var ui = CreateUiApi();

        var result = ui.Stack();

        var json = RenderNodeJsonSerializer.Serialize(result);
        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("duetspad-stack", (string?)json["attributes"]!["class"]);
        Assert.Empty(json["children"]!.AsArray());
    }

    [Fact]
    public void Stack_with_children_renders_each_via_pipeline()
    {
        var ui = CreateUiApi();
        var children = new object?[] { "hello", "world" };

        var result = ui.Stack(children);

        var json = RenderNodeJsonSerializer.Serialize(result);
        Assert.Equal(2, json["children"]!.AsArray().Count);
        Assert.Equal("hello", (string?)json["children"]![0]!["value"]);
        Assert.Equal("world", (string?)json["children"]![1]!["value"]);
    }

    // ── Positive: Element ─────────────────────────────────────────────────

    [Fact]
    public void Element_with_attribute_dict_and_child_list_builds_correct_node()
    {
        var ui = CreateUiApi();
        var attrs = new Dictionary<string, object?> { ["id"] = "x", ["class"] = "card" };
        var childText = new Duets.Pad.Rendering.Text("hi");

        var result = ui.Element("div", attrs, new object?[] { childText });

        var json = RenderNodeJsonSerializer.Serialize(result);
        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("x", (string?)json["attributes"]!["id"]);
        Assert.Equal("card", (string?)json["attributes"]!["class"]);
        Assert.Single(json["children"]!.AsArray());
        Assert.Equal("text", (string?)json["children"]![0]!["kind"]);
        Assert.Equal("hi", (string?)json["children"]![0]!["value"]);
    }

    [Fact]
    public void Element_with_null_attributes_uses_empty_attributes()
    {
        var ui = CreateUiApi();

        var result = ui.Element("div", null, null);

        var json = RenderNodeJsonSerializer.Serialize(result);
        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Empty(json["attributes"]!.AsObject());
        Assert.Empty(json["children"]!.AsArray());
    }

    [Fact]
    public void Element_with_null_attribute_value_preserves_null_boolean_attribute()
    {
        var ui = CreateUiApi();
        var attrs = new Dictionary<string, object?> { ["hidden"] = null };

        var result = ui.Element("div", attrs);

        var json = RenderNodeJsonSerializer.Serialize(result);
        Assert.True(json["attributes"]!.AsObject().ContainsKey("hidden"));
        Assert.Null(json["attributes"]!["hidden"]);
    }

    [Fact]
    public void Element_tag_x_debug_panel_is_allowed()
    {
        var ui = CreateUiApi();

        var result = ui.Element("x-debug-panel");

        var json = RenderNodeJsonSerializer.Serialize(result);
        Assert.Equal("x-debug-panel", (string?)json["tag"]);
    }

    // ── Positive: Table ───────────────────────────────────────────────────

    [Fact]
    public void Table_with_single_row_builds_thead_and_tbody()
    {
        var ui = CreateUiApi();
        var rows = new object?[]
        {
            new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 },
        };

        var result = ui.Table(rows);

        var json = RenderNodeJsonSerializer.Serialize(result);
        Assert.Equal("table", (string?)json["tag"]);
        Assert.Equal("duetspad-table", (string?)json["attributes"]!["class"]);

        var children = json["children"]!.AsArray();
        Assert.Equal(2, children.Count);
        Assert.Equal("thead", (string?)children[0]!["tag"]);
        Assert.Equal("tbody", (string?)children[1]!["tag"]);

        // thead > tr > th*
        var headerRow = children[0]!["children"]![0];
        Assert.Equal("tr", (string?)headerRow!["tag"]);
        var headers = headerRow["children"]!.AsArray();
        Assert.Equal(2, headers.Count);
        Assert.Equal("a", (string?)headers[0]!["children"]![0]!["value"]);
        Assert.Equal("b", (string?)headers[1]!["children"]![0]!["value"]);

        // tbody > tr > td*
        var bodyRow = children[1]!["children"]![0];
        Assert.Equal("tr", (string?)bodyRow!["tag"]);
        var cells = bodyRow["children"]!.AsArray();
        Assert.Equal(2, cells.Count);
    }

    [Fact]
    public void Table_with_empty_rows_returns_table_with_empty_thead_and_tbody()
    {
        var ui = CreateUiApi();

        var result = ui.Table(Array.Empty<object?>());

        var json = RenderNodeJsonSerializer.Serialize(result);
        Assert.Equal("table", (string?)json["tag"]);
        var children = json["children"]!.AsArray();
        Assert.Equal(2, children.Count);
        Assert.Equal("thead", (string?)children[0]!["tag"]);
        Assert.Equal("tbody", (string?)children[1]!["tag"]);
    }

    [Fact]
    public void Table_with_explicit_columns_option_uses_those_columns()
    {
        var ui = CreateUiApi();
        var rows = new object?[]
        {
            new Dictionary<string, object?>
            {
                ["a"] = 1,
                ["b"] = 2,
                ["c"] = 3,
            },
        };
        var options = new Dictionary<string, object?> { ["columns"] = new object[] { "b", "a" } };

        var result = ui.Table(rows, options);

        var json = RenderNodeJsonSerializer.Serialize(result);
        var headerRow = json["children"]![0]!["children"]![0];
        var headers = headerRow!["children"]!.AsArray();
        Assert.Equal(2, headers.Count);
        Assert.Equal("b", (string?)headers[0]!["children"]![0]!["value"]);
        Assert.Equal("a", (string?)headers[1]!["children"]![0]!["value"]);
    }

    [Fact]
    public void Table_missing_column_in_row_produces_empty_text_cell()
    {
        var ui = CreateUiApi();
        var rows = new object?[] { new Dictionary<string, object?> { ["a"] = 1 } };
        var options = new Dictionary<string, object?> { ["columns"] = new object[] { "a", "z" } };

        var result = ui.Table(rows, options);

        var json = RenderNodeJsonSerializer.Serialize(result);
        var bodyRow = json["children"]![1]!["children"]![0];
        var cells = bodyRow!["children"]!.AsArray();
        Assert.Equal(2, cells.Count);
        // second cell ("z" column) is empty text
        Assert.Equal("", (string?)cells[1]!["children"]![0]!["value"]);
    }

    // ── Positive: Table with CLR object rows ──────────────────────────────

    [Fact]
    public void Table_with_clr_object_rows_uses_projected_properties_as_columns()
    {
        var ui = CreateUiApi();
        var rows = new object?[] { new SimpleRow("Alice", 30), new SimpleRow("Bob", 25) };

        var result = ui.Table(rows);

        var json = RenderNodeJsonSerializer.Serialize(result);
        Assert.Equal("table", (string?)json["tag"]);
        Assert.Equal("duetspad-table", (string?)json["attributes"]!["class"]);

        var headerRow = json["children"]![0]!["children"]![0];
        var headers = headerRow!["children"]!.AsArray();
        Assert.Equal(2, headers.Count);
        Assert.Equal("Name", (string?)headers[0]!["children"]![0]!["value"]);
        Assert.Equal("Age", (string?)headers[1]!["children"]![0]!["value"]);

        var bodyRow0 = json["children"]![1]!["children"]![0];
        var cells0 = bodyRow0!["children"]!.AsArray();
        Assert.Equal("Alice", (string?)cells0[0]!["children"]![0]!["value"]);
        Assert.Equal("30", (string?)cells0[1]!["children"]![0]!["value"]);
    }

    [Fact]
    public void Table_with_explicit_columns_over_clr_object_rows_honors_column_order()
    {
        var ui = CreateUiApi();
        var rows = new object?[] { new SimpleRow("Alice", 30) };
        var options = new Dictionary<string, object?>
        {
            ["columns"] = new object[] { "Age", "Name" },
        };

        var result = ui.Table(rows, options);

        var json = RenderNodeJsonSerializer.Serialize(result);
        var headerRow = json["children"]![0]!["children"]![0];
        var headers = headerRow!["children"]!.AsArray();
        Assert.Equal(2, headers.Count);
        Assert.Equal("Age", (string?)headers[0]!["children"]![0]!["value"]);
        Assert.Equal("Name", (string?)headers[1]!["children"]![0]!["value"]);
    }

    // ── Negative: Table ───────────────────────────────────────────────────

    [Fact]
    public void Element_script_tag_throws()
    {
        var ui = CreateUiApi();

        Assert.Throws<ArgumentException>(() => ui.Element("script"));
    }

    [Fact]
    public void Element_iframe_tag_throws()
    {
        var ui = CreateUiApi();

        Assert.Throws<ArgumentException>(() => ui.Element("iframe"));
    }

    [Fact]
    public void Element_href_javascript_url_throws()
    {
        var ui = CreateUiApi();
        var attrs = new Dictionary<string, object?> { ["href"] = "javascript:alert(1)" };

        Assert.Throws<ArgumentException>(() => ui.Element("a", attrs));
    }

    [Fact]
    public void Element_on_event_attribute_throws()
    {
        var ui = CreateUiApi();
        var attrs = new Dictionary<string, object?> { ["onclick"] = "alert(1)" };

        Assert.Throws<ArgumentException>(() => ui.Element("div", attrs));
    }

    [Fact]
    public void Element_srcdoc_attribute_throws()
    {
        var ui = CreateUiApi();
        var attrs = new Dictionary<string, object?> { ["srcdoc"] = "<p>html</p>" };

        Assert.Throws<ArgumentException>(() => ui.Element("div", attrs));
    }

    // ── Negative: Table ───────────────────────────────────────────────────

    [Fact]
    public void Table_with_non_enumerable_rows_throws()
    {
        var ui = CreateUiApi();

        Assert.Throws<ArgumentException>(() => ui.Table(1));
    }

    [Fact]
    public void Table_with_null_row_throws()
    {
        var ui = CreateUiApi();

        Assert.Throws<ArgumentException>(() => ui.Table(new object?[] { null }));
    }

    [Fact]
    public void Table_with_string_rows_throws()
    {
        var ui = CreateUiApi();

        Assert.Throws<ArgumentException>(() => ui.Table("not-an-array"));
    }

    [Fact]
    public void Table_with_primitive_row_throws()
    {
        var ui = CreateUiApi();

        Assert.Throws<ArgumentException>(() => ui.Table(new object?[] { 42 }));
    }

    [Fact]
    public void Table_with_array_row_throws()
    {
        var ui = CreateUiApi();

        Assert.Throws<ArgumentException>(() => ui.Table(new object?[] { new object[] { 1, 2 } }));
    }

    [Fact]
    public void Table_with_duplicate_columns_throws()
    {
        var ui = CreateUiApi();
        var rows = new object?[] { new Dictionary<string, object?> { ["a"] = 1 } };
        var options = new Dictionary<string, object?> { ["columns"] = new object[] { "a", "a" } };

        var ex = Assert.Throws<ArgumentException>(() => ui.Table(rows, options));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Table_with_non_string_column_element_throws_ArgumentException()
    {
        var ui = CreateUiApi();
        var rows = new object?[] { new Dictionary<string, object?> { ["a"] = 1 } };
        // columns list contains an int instead of a string — must be rejected
        var options = new Dictionary<string, object?> { ["columns"] = new object?[] { 1 } };

        var ex = Assert.Throws<ArgumentException>(() => ui.Table(rows, options));
        Assert.Equal("options", ex.ParamName);
        Assert.Contains("strings", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Table_with_string_element_row_throws_invalid_row()
    {
        var ui = CreateUiApi();

        var ex = Assert.Throws<ArgumentException>(() => ui.Table(new object?[] { "a string" }));
        Assert.Contains("invalid row", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Table_with_int_element_row_throws_invalid_row()
    {
        var ui = CreateUiApi();

        var ex = Assert.Throws<ArgumentException>(() => ui.Table(new object?[] { 42 }));
        Assert.Contains("invalid row", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Table_with_array_element_row_throws_invalid_row()
    {
        var ui = CreateUiApi();

        var ex = Assert.Throws<ArgumentException>(() =>
            ui.Table(new object?[] { new object[] { 1, 2 } })
        );
        Assert.Contains("invalid row", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helper types ──────────────────────────────────────────────────────

    private sealed record SimpleRow(string Name, int Age);
}
