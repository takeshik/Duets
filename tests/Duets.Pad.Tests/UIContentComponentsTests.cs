using Duets.Pad;
using Duets.Pad.Rendering;

namespace Duets.Pad.Tests;

public sealed class UIContentComponentsTests
{
    private static UIGlobal CreateUIGlobal() => new(new DisplayRenderer([]), DumpOptions.Default);

    [Fact]
    public void DataGrid_renders_labeled_content_and_rebases_interactions()
    {
        var ui = CreateUIGlobal();
        var result = ui.DataGrid(
            new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["label"] = "Environment",
                    ["content"] = ui.Status(
                        "Ready",
                        new Dictionary<string, object?> { ["color"] = "green" }
                    ),
                },
                new Dictionary<string, object?>
                {
                    ["label"] = "Action",
                    ["content"] = ui.Button("Refresh", () => { }),
                },
            }
        );

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("datagrid", (string?)json["attributes"]!["class"]);
        var items = json["children"]!.AsArray();
        Assert.Equal(2, items.Count);
        Assert.Equal("datagrid-item", (string?)items[0]!["attributes"]!["class"]);
        Assert.Equal("datagrid-title", (string?)items[0]!["children"]![0]!["attributes"]!["class"]);
        Assert.Equal("Environment", (string?)items[0]!["children"]![0]!["children"]![0]!["value"]);
        Assert.Equal(
            "datagrid-content",
            (string?)items[1]!["children"]![1]!["attributes"]!["class"]
        );

        var interaction = Assert.Single(result.Interactions);
        Assert.Equal([1, 1, 0], interaction.Target.Segments);
    }

    [Fact]
    public void DataGrid_requires_an_array_of_labeled_content_items()
    {
        var ui = CreateUIGlobal();

        Assert.Throws<ArgumentException>(() => ui.DataGrid(null));
        Assert.Throws<ArgumentException>(() => ui.DataGrid("not an array"));
        Assert.Throws<ArgumentException>(() => ui.DataGrid(new object?[] { 42 }));
        Assert.Throws<ArgumentException>(() =>
            ui.DataGrid(new object?[] { new { content = "missing label" } })
        );
        Assert.Throws<ArgumentException>(() =>
            ui.DataGrid(new object?[] { new { label = "missing content" } })
        );
        Assert.Throws<ArgumentException>(() =>
            ui.DataGrid(new object?[] { new { label = " ", content = "value" } })
        );
    }

    [Fact]
    public void EmptySpace_renders_optional_parts_and_rebases_action_interaction()
    {
        var ui = CreateUIGlobal();
        var result = ui.EmptySpace(
            "No warnings",
            new Dictionary<string, object?>
            {
                ["message"] = "The latest run completed cleanly.",
                ["icon"] = "circle-check",
                ["action"] = ui.Button("Run again", () => { }),
            }
        );

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("empty", (string?)json["attributes"]!["class"]);
        var children = json["children"]!.AsArray();
        Assert.Equal(4, children.Count);
        Assert.Equal("empty-icon", (string?)children[0]!["attributes"]!["class"]);
        Assert.Equal(
            "ti ti-circle-check",
            (string?)children[0]!["children"]![0]!["attributes"]!["class"]
        );
        Assert.Equal("empty-title", (string?)children[1]!["attributes"]!["class"]);
        Assert.Equal("No warnings", (string?)children[1]!["children"]![0]!["value"]);
        Assert.Equal(
            "empty-subtitle text-secondary",
            (string?)children[2]!["attributes"]!["class"]
        );
        Assert.Equal("empty-action", (string?)children[3]!["attributes"]!["class"]);

        var interaction = Assert.Single(result.Interactions);
        Assert.Equal([3, 0], interaction.Target.Segments);
    }

    [Fact]
    public void EmptySpace_with_only_title_has_no_optional_parts()
    {
        var ui = CreateUIGlobal();

        var result = ui.EmptySpace("Nothing selected");

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        var child = Assert.Single(json["children"]!.AsArray());
        Assert.Equal("empty-title", (string?)child!["attributes"]!["class"]);
        Assert.Empty(result.Interactions);
    }

    [Fact]
    public void Code_renders_untrusted_input_as_text_inside_semantic_code_block()
    {
        var ui = CreateUIGlobal();

        var result = ui.Code(
            "<script>alert('xss')</script>\nconst value = 1;",
            new Dictionary<string, object?> { ["wrap"] = true }
        );

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("pre", (string?)json["tag"]);
        Assert.Equal(
            "duetspad-preformatted duetspad-code duetspad-preformatted-wrap",
            (string?)json["attributes"]!["class"]
        );
        Assert.Equal("code", (string?)json["children"]![0]!["tag"]);
        Assert.Equal("text", (string?)json["children"]![0]!["children"]![0]!["kind"]);
        Assert.Equal(
            "<script>alert('xss')</script>\nconst value = 1;",
            (string?)json["children"]![0]!["children"]![0]!["value"]
        );
    }

    [Fact]
    public void Preformatted_renders_text_directly_and_does_not_wrap_by_default()
    {
        var ui = CreateUIGlobal();

        var result = ui.Preformatted("line 1\n  line 2");

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("pre", (string?)json["tag"]);
        Assert.Equal("duetspad-preformatted", (string?)json["attributes"]!["class"]);
        Assert.Equal("text", (string?)json["children"]![0]!["kind"]);
        Assert.Equal("line 1\n  line 2", (string?)json["children"]![0]!["value"]);
    }

    [Fact]
    public void Disclosure_renders_native_details_and_rebases_content_interaction()
    {
        var ui = CreateUIGlobal();
        var result = ui.Disclosure(
            "Diagnostic details",
            ui.Button("Copy", () => { }),
            new Dictionary<string, object?> { ["open"] = true }
        );

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.Equal("details", (string?)json["tag"]);
        Assert.Equal("duetspad-disclosure", (string?)json["attributes"]!["class"]);
        Assert.True(json["attributes"]!.AsObject().ContainsKey("open"));
        Assert.Null(json["attributes"]!["open"]);
        Assert.Equal("summary", (string?)json["children"]![0]!["tag"]);
        Assert.Equal(
            "Diagnostic details",
            (string?)json["children"]![0]!["children"]![0]!["value"]
        );
        Assert.Equal(
            "duetspad-disclosure-content",
            (string?)json["children"]![1]!["attributes"]!["class"]
        );

        var interaction = Assert.Single(result.Interactions);
        Assert.Equal([1, 0], interaction.Target.Segments);
    }

    [Fact]
    public void Disclosure_is_closed_by_default()
    {
        var ui = CreateUIGlobal();

        var result = ui.Disclosure("Details", "content");

        var json = RenderNodeJsonSerializer.Serialize(result.Body);
        Assert.False(json["attributes"]!.AsObject().ContainsKey("open"));
    }
}
