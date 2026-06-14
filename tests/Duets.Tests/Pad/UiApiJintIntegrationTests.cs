using Duets.Jint;
using Duets.Pad;
using Duets.Pad.Rendering;
using Jint;

namespace Duets.Tests.Pad;

/// <summary>
/// End-to-end integration tests that confirm Jint's CLR marshaling behavior:
/// - JS object literals → <see cref="System.Dynamic.ExpandoObject"/> (implements <see cref="System.Collections.Generic.IDictionary{TKey,TValue}"/>)
/// - JS arrays → <see cref="object"/>[] (non-string <see cref="System.Collections.IEnumerable"/>)
/// - Jint maps camelCase JS calls to PascalCase CLR methods
/// </summary>
public sealed class UiApiJintIntegrationTests
{
    private static async Task<DuetsSession> CreateSessionAsync()
    {
        return await DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));
    }

    [Fact]
    public async Task Element_built_from_js_object_literal_and_array_marshals_correctly()
    {
        using var session = await CreateSessionAsync();
        var ui = new UiApi(() => new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        // JS: ui.element('div', { id: 'x' }, [ui.text('hi')])
        var result = session.Evaluate("ui.element('div', { id: 'x' }, [ui.text('hi')])");
        var content = Assert.IsAssignableFrom<DisplayContent>(result.ToObject());

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("x", (string?)json["attributes"]!["id"]);
        Assert.Single(json["children"]!.AsArray());
        Assert.Equal("text", (string?)json["children"]![0]!["kind"]);
        Assert.Equal("hi", (string?)json["children"]![0]!["value"]);
    }

    [Fact]
    public async Task Label_from_js_serializes_correctly()
    {
        using var session = await CreateSessionAsync();
        var ui = new UiApi(() => new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var result = session.Evaluate("ui.label('Hello')");
        var content = Assert.IsAssignableFrom<DisplayContent>(result.ToObject());

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("span", (string?)json["tag"]);
        Assert.Equal("duetspad-label", (string?)json["attributes"]!["class"]);
        Assert.Equal("Hello", (string?)json["children"]![0]!["value"]);
    }

    [Fact]
    public async Task Table_from_js_serializes_correctly()
    {
        using var session = await CreateSessionAsync();
        var ui = new UiApi(() => new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var result = session.Evaluate("ui.table([{ a: 1, b: 2 }])");
        var content = Assert.IsAssignableFrom<DisplayContent>(result.ToObject());

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("element", (string?)json["kind"]);
        Assert.Equal("table", (string?)json["tag"]);
        Assert.Equal("duetspad-table", (string?)json["attributes"]!["class"]);

        var children = json["children"]!.AsArray();
        Assert.Equal(2, children.Count);
        Assert.Equal("thead", (string?)children[0]!["tag"]);
        Assert.Equal("tbody", (string?)children[1]!["tag"]);

        // Verify headers: a, b
        var headers = children[0]!["children"]![0]!["children"]!.AsArray();
        Assert.Equal(2, headers.Count);
        Assert.Equal("a", (string?)headers[0]!["children"]![0]!["value"]);
        Assert.Equal("b", (string?)headers[1]!["children"]![0]!["value"]);

        // Verify one body row
        var bodyRows = children[1]!["children"]!.AsArray();
        Assert.Single(bodyRows);
    }

    [Fact]
    public async Task Element_script_tag_from_js_throws()
    {
        using var session = await CreateSessionAsync();
        var ui = new UiApi(() => new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        Assert.ThrowsAny<Exception>(() => session.Evaluate("ui.element('script')"));
    }

    [Fact]
    public async Task Button_from_js_returns_button_content_with_click_interaction()
    {
        using var session = await CreateSessionAsync();
        var ui = new UiApi(() => new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var result = session.Evaluate("ui.button('Run', () => {})");
        var content = Assert.IsAssignableFrom<DisplayContent>(result.ToObject());

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("button", (string?)json["tag"]);
        Assert.Equal("Run", (string?)json["children"]![0]!["value"]);

        var interaction = Assert.Single(content.Interactions);
        Assert.Equal(InteractionEvent.Click, interaction.Event);
    }
}
