using Duets.Pad;
using Duets.Pad.Rendering;

namespace Duets.Tests.Pad;

public sealed class UIGlobalTests
{
    private static UIGlobal CreateUIGlobal() => new(new DisplayRenderer([]), DumpOptions.Default);

    // Positive: RawHtml

    [Fact]
    public void RawHtml_returns_RawHtml_node()
    {
        var ui = CreateUIGlobal();

        var result = ui.RawHtml("<strong>hello</strong>");

        var node = Assert.IsType<RawHtml>(result.Body);
        Assert.Equal("<strong>hello</strong>", node.Content);
    }

    // Positive: Text

    [Fact]
    public void Text_returns_Text_node()
    {
        var ui = CreateUIGlobal();

        var result = ui.Text("hello");

        var node = Assert.IsType<Duets.Pad.Rendering.Text>(result.Body);
        Assert.Equal("hello", node.Value);
    }

    // Positive: Label

    [Fact]
    public void Label_returns_span_with_duetspad_label_class_and_text_child()
    {
        var ui = CreateUIGlobal();

        var result = ui.Label("my label");

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("span", (string?)json["tag"]);
        Assert.Equal("duetspad-label", (string?)json["attributes"]!["class"]);
        Assert.Single(json["children"]!.AsArray());
        Assert.Equal("text", (string?)json["children"]![0]!["kind"]);
        Assert.Equal("my label", (string?)json["children"]![0]!["value"]);
    }

    // Positive: Tabler components

    [Fact]
    public void Badge_returns_tabler_badge_with_options()
    {
        var ui = CreateUIGlobal();

        var result = ui.Badge(
            "new",
            new Dictionary<string, object?>
            {
                ["color"] = "green",
                ["pill"] = true,
                ["outline"] = true,
            }
        );

        var node = Assert.IsType<Element>(result.Body);
        Assert.Equal("span", node.Tag);

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal(
            "badge bg-green-lt badge-pill badge-outline",
            (string?)json["attributes"]!["class"]
        );
        Assert.Equal("new", (string?)json["children"]![0]!["value"]);
    }

    [Fact]
    public void Alert_returns_tabler_alert_with_title_and_role()
    {
        var ui = CreateUIGlobal();

        var result = ui.Alert(
            "Saved",
            new Dictionary<string, object?> { ["variant"] = "success", ["title"] = "Done" }
        );

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("alert alert-success", (string?)json["attributes"]!["class"]);
        Assert.Equal("alert", (string?)json["attributes"]!["role"]);
        Assert.Equal("alert-title", (string?)json["children"]![0]!["attributes"]!["class"]);
        Assert.Equal("Done", (string?)json["children"]![0]!["children"]![0]!["value"]);
        Assert.Equal("Saved", (string?)json["children"]![1]!["value"]);
    }

    [Fact]
    public void Spinner_returns_tabler_spinner_with_options()
    {
        var ui = CreateUIGlobal();

        var result = ui.Spinner(
            new Dictionary<string, object?> { ["color"] = "blue", ["small"] = true }
        );

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal(
            "spinner-border text-blue spinner-border-sm",
            (string?)json["attributes"]!["class"]
        );
        Assert.Equal("status", (string?)json["attributes"]!["role"]);
        Assert.Empty(json["children"]!.AsArray());
    }

    [Fact]
    public void Status_returns_tabler_status_with_dot_and_text()
    {
        var ui = CreateUIGlobal();

        var result = ui.Status(
            "Online",
            new Dictionary<string, object?> { ["color"] = "green", ["animated"] = true }
        );

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("span", (string?)json["tag"]);
        Assert.Equal("status status-green", (string?)json["attributes"]!["class"]);
        Assert.Equal("span", (string?)json["children"]![0]!["tag"]);
        Assert.Equal(
            "status-dot status-dot-animated",
            (string?)json["children"]![0]!["attributes"]!["class"]
        );
        Assert.Equal("Online", (string?)json["children"]![1]!["value"]);
    }

    [Fact]
    public void Icon_returns_tabler_icon_with_size_and_color()
    {
        var ui = CreateUIGlobal();

        var result = ui.Icon(
            "alert-triangle",
            new Dictionary<string, object?> { ["size"] = 24, ["color"] = "warning" }
        );

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("i", (string?)json["tag"]);
        Assert.Equal("ti ti-alert-triangle text-warning", (string?)json["attributes"]!["class"]);
        Assert.Equal("font-size: 24px", (string?)json["attributes"]!["style"]);
        Assert.Empty(json["children"]!.AsArray());
    }

    [Fact]
    public void Progress_returns_tabler_progress_with_patchable_bar_attributes()
    {
        var ui = CreateUIGlobal();

        var result = ui.Progress(
            42.5,
            new Dictionary<string, object?> { ["color"] = "green", ["label"] = "42.5%" }
        );

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("progress", (string?)json["attributes"]!["class"]);

        var bar = json["children"]![0];
        Assert.Equal("div", (string?)bar!["tag"]);
        Assert.Equal("progress-bar bg-green", (string?)bar["attributes"]!["class"]);
        Assert.Equal("width: 42.5%", (string?)bar["attributes"]!["style"]);
        Assert.Equal("progressbar", (string?)bar["attributes"]!["role"]);
        Assert.Equal("42.5", (string?)bar["attributes"]!["aria-valuenow"]);
        Assert.Equal("0", (string?)bar["attributes"]!["aria-valuemin"]);
        Assert.Equal("100", (string?)bar["attributes"]!["aria-valuemax"]);
        Assert.Equal("42.5%", (string?)bar["children"]![0]!["value"]);
    }

    [Fact]
    public void Alert_without_options_defaults_to_info_without_title()
    {
        var ui = CreateUIGlobal();

        var result = ui.Alert("Heads up");

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("alert alert-info", (string?)json["attributes"]!["class"]);
        Assert.Single(json["children"]!.AsArray());
        Assert.Equal("Heads up", (string?)json["children"]![0]!["value"]);
    }

    // Positive: Stack

    [Fact]
    public void Stack_with_no_children_returns_empty_div()
    {
        var ui = CreateUIGlobal();

        var result = ui.Stack();

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("duetspad-stack", (string?)json["attributes"]!["class"]);
        Assert.Empty(json["children"]!.AsArray());
    }

    [Fact]
    public void Stack_with_children_renders_each_via_pipeline()
    {
        var ui = CreateUIGlobal();
        var children = new object?[] { "hello", "world" };

        var result = ui.Stack(children);

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal(2, json["children"]!.AsArray().Count);
        Assert.Equal("hello", (string?)json["children"]![0]!["value"]);
        Assert.Equal("world", (string?)json["children"]![1]!["value"]);
    }

    [Fact]
    public void Button_returns_button_body_and_pending_click_interaction()
    {
        var ui = CreateUIGlobal();
        var clicked = false;

        var result = ui.Button("Run", () => clicked = true);

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("button", (string?)json["tag"]);
        Assert.Equal("button", (string?)json["attributes"]!["type"]);
        Assert.Equal("Run", (string?)json["children"]![0]!["value"]);

        var interaction = Assert.Single(result.Interactions);
        Assert.Equal(InteractionEvent.Click, interaction.Event);

        interaction.Handler();
        Assert.True(clicked);
    }

    [Fact]
    public void Disabled_button_returns_button_body_without_pending_interaction()
    {
        var ui = CreateUIGlobal();

        var result = ui.Button(
            "Run",
            () => throw new InvalidOperationException("must not be registered"),
            new Dictionary<string, object?> { ["disabled"] = true }
        );

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("button", (string?)json["tag"]);
        Assert.True(json["attributes"]!.AsObject().ContainsKey("disabled"));
        Assert.Empty(result.Interactions);
    }

    // Positive: Element

    [Fact]
    public void Element_with_attribute_dict_and_child_list_builds_correct_node()
    {
        var ui = CreateUIGlobal();
        var attrs = new Dictionary<string, object?> { ["id"] = "x", ["class"] = "card" };
        var childText = new Duets.Pad.Rendering.Text("hi");

        var result = ui.Element("div", attrs, new object?[] { childText });

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
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
        var ui = CreateUIGlobal();

        var result = ui.Element("div", null, null);

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Empty(json["attributes"]!.AsObject());
        Assert.Empty(json["children"]!.AsArray());
    }

    [Fact]
    public void Element_with_null_attribute_value_preserves_null_boolean_attribute()
    {
        var ui = CreateUIGlobal();
        var attrs = new Dictionary<string, object?> { ["hidden"] = null };

        var result = ui.Element("div", attrs);

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.True(json["attributes"]!.AsObject().ContainsKey("hidden"));
        Assert.Null(json["attributes"]!["hidden"]);
    }

    [Fact]
    public void Element_tag_x_debug_panel_is_allowed()
    {
        var ui = CreateUIGlobal();

        var result = ui.Element("x-debug-panel");

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("x-debug-panel", (string?)json["tag"]);
    }

    // Positive: Table

    [Fact]
    public void Table_with_single_row_builds_thead_and_tbody()
    {
        var ui = CreateUIGlobal();
        var rows = new object?[]
        {
            new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 },
        };

        var result = ui.Table(rows);

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
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
        var ui = CreateUIGlobal();

        var result = ui.Table(Array.Empty<object?>());

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("table", (string?)json["tag"]);
        var children = json["children"]!.AsArray();
        Assert.Equal(2, children.Count);
        Assert.Equal("thead", (string?)children[0]!["tag"]);
        Assert.Equal("tbody", (string?)children[1]!["tag"]);
    }

    [Fact]
    public void Table_with_explicit_columns_option_uses_those_columns()
    {
        var ui = CreateUIGlobal();
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

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        var headerRow = json["children"]![0]!["children"]![0];
        var headers = headerRow!["children"]!.AsArray();
        Assert.Equal(2, headers.Count);
        Assert.Equal("b", (string?)headers[0]!["children"]![0]!["value"]);
        Assert.Equal("a", (string?)headers[1]!["children"]![0]!["value"]);
    }

    [Fact]
    public void Table_missing_column_in_row_produces_empty_text_cell()
    {
        var ui = CreateUIGlobal();
        var rows = new object?[] { new Dictionary<string, object?> { ["a"] = 1 } };
        var options = new Dictionary<string, object?> { ["columns"] = new object[] { "a", "z" } };

        var result = ui.Table(rows, options);

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        var bodyRow = json["children"]![1]!["children"]![0];
        var cells = bodyRow!["children"]!.AsArray();
        Assert.Equal(2, cells.Count);
        // second cell ("z" column) is empty text
        Assert.Equal("", (string?)cells[1]!["children"]![0]!["value"]);
    }

    // Positive: Table with CLR object rows

    [Fact]
    public void Table_with_clr_object_rows_uses_projected_properties_as_columns()
    {
        var ui = CreateUIGlobal();
        var rows = new object?[] { new SimpleRow("Alice", 30), new SimpleRow("Bob", 25) };

        var result = ui.Table(rows);

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
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
        var ui = CreateUIGlobal();
        var rows = new object?[] { new SimpleRow("Alice", 30) };
        var options = new Dictionary<string, object?>
        {
            ["columns"] = new object[] { "Age", "Name" },
        };

        var result = ui.Table(rows, options);

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        var headerRow = json["children"]![0]!["children"]![0];
        var headers = headerRow!["children"]!.AsArray();
        Assert.Equal(2, headers.Count);
        Assert.Equal("Age", (string?)headers[0]!["children"]![0]!["value"]);
        Assert.Equal("Name", (string?)headers[1]!["children"]![0]!["value"]);
    }

    // Negative: Table

    [Fact]
    public void Element_script_tag_throws()
    {
        var ui = CreateUIGlobal();

        Assert.Throws<ArgumentException>(() => ui.Element("script"));
    }

    [Fact]
    public void Element_iframe_tag_throws()
    {
        var ui = CreateUIGlobal();

        Assert.Throws<ArgumentException>(() => ui.Element("iframe"));
    }

    [Fact]
    public void Element_href_javascript_url_throws()
    {
        var ui = CreateUIGlobal();
        var attrs = new Dictionary<string, object?> { ["href"] = "javascript:alert(1)" };

        Assert.Throws<ArgumentException>(() => ui.Element("a", attrs));
    }

    [Fact]
    public void Element_on_event_attribute_throws()
    {
        var ui = CreateUIGlobal();
        var attrs = new Dictionary<string, object?> { ["onclick"] = "alert(1)" };

        Assert.Throws<ArgumentException>(() => ui.Element("div", attrs));
    }

    [Fact]
    public void Element_srcdoc_attribute_throws()
    {
        var ui = CreateUIGlobal();
        var attrs = new Dictionary<string, object?> { ["srcdoc"] = "<p>html</p>" };

        Assert.Throws<ArgumentException>(() => ui.Element("div", attrs));
    }

    // Negative: Tabler components

    [Fact]
    public void Badge_with_null_text_throws()
    {
        var ui = CreateUIGlobal();

        Assert.Throws<ArgumentNullException>(() => ui.Badge(null!));
    }

    [Fact]
    public void Alert_with_invalid_variant_throws()
    {
        var ui = CreateUIGlobal();

        var ex = Assert.Throws<ArgumentException>(() =>
            ui.Alert("Saved", new Dictionary<string, object?> { ["variant"] = "primary" })
        );
        Assert.Contains("variant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Spinner_with_non_object_options_throws()
    {
        var ui = CreateUIGlobal();

        var ex = Assert.Throws<ArgumentException>(() => ui.Spinner(42));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void Icon_with_null_name_throws()
    {
        var ui = CreateUIGlobal();

        Assert.Throws<ArgumentNullException>(() => ui.Icon(null!));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Progress_with_out_of_range_value_throws(double value)
    {
        var ui = CreateUIGlobal();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ui.Progress(value));
        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public void Progress_with_null_value_throws()
    {
        var ui = CreateUIGlobal();

        var ex = Assert.Throws<ArgumentException>(() => ui.Progress(null));
        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public void Progress_with_string_value_throws()
    {
        var ui = CreateUIGlobal();

        var ex = Assert.Throws<ArgumentException>(() => ui.Progress("50"));
        Assert.Equal("value", ex.ParamName);
    }

    // Negative: Table

    [Fact]
    public void Table_with_non_enumerable_rows_throws()
    {
        var ui = CreateUIGlobal();

        Assert.Throws<ArgumentException>(() => ui.Table(1));
    }

    [Fact]
    public void Table_with_null_row_throws()
    {
        var ui = CreateUIGlobal();

        Assert.Throws<ArgumentException>(() => ui.Table(new object?[] { null }));
    }

    [Fact]
    public void Table_with_string_rows_throws()
    {
        var ui = CreateUIGlobal();

        Assert.Throws<ArgumentException>(() => ui.Table("not-an-array"));
    }

    [Fact]
    public void Table_with_primitive_row_throws()
    {
        var ui = CreateUIGlobal();

        Assert.Throws<ArgumentException>(() => ui.Table(new object?[] { 42 }));
    }

    [Fact]
    public void Table_with_array_row_throws()
    {
        var ui = CreateUIGlobal();

        Assert.Throws<ArgumentException>(() => ui.Table(new object?[] { new object[] { 1, 2 } }));
    }

    [Fact]
    public void Table_with_duplicate_columns_throws()
    {
        var ui = CreateUIGlobal();
        var rows = new object?[] { new Dictionary<string, object?> { ["a"] = 1 } };
        var options = new Dictionary<string, object?> { ["columns"] = new object[] { "a", "a" } };

        var ex = Assert.Throws<ArgumentException>(() => ui.Table(rows, options));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Table_with_non_string_column_element_throws_ArgumentException()
    {
        var ui = CreateUIGlobal();
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
        var ui = CreateUIGlobal();

        var ex = Assert.Throws<ArgumentException>(() => ui.Table(new object?[] { "a string" }));
        Assert.Contains("invalid row", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Table_with_int_element_row_throws_invalid_row()
    {
        var ui = CreateUIGlobal();

        var ex = Assert.Throws<ArgumentException>(() => ui.Table(new object?[] { 42 }));
        Assert.Contains("invalid row", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Table_with_array_element_row_throws_invalid_row()
    {
        var ui = CreateUIGlobal();

        var ex = Assert.Throws<ArgumentException>(() =>
            ui.Table(new object?[] { new object[] { 1, 2 } })
        );
        Assert.Contains("invalid row", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Helper types

    private sealed record SimpleRow(string Name, int Age);
}
