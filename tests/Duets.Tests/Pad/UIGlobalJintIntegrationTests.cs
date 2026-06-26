using Duets.Jint;
using Duets.Pad;
using Duets.Pad.Rendering;
using Duets.Tests.TestSupport;
using Jint;

namespace Duets.Tests.Pad;

/// <summary>
/// End-to-end integration tests that confirm Jint's CLR marshaling behavior:
/// - JS object literals → <see cref="System.Dynamic.ExpandoObject"/> (implements <see cref="System.Collections.Generic.IDictionary{TKey,TValue}"/>)
/// - JS arrays → <see cref="object"/>[] (non-string <see cref="System.Collections.IEnumerable"/>)
/// - Jint maps camelCase JS calls to PascalCase CLR methods
/// </summary>
public sealed class UIGlobalJintIntegrationTests
{
    private static async Task<DuetsSession> CreateSessionAsync()
    {
        return await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
    }

    [Fact]
    public async Task Element_built_from_js_object_literal_and_array_marshals_correctly()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
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
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
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
    public async Task Tabler_components_from_js_serialize_correctly()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var badge = EvaluateDisplayContent(
            session,
            "ui.badge('new', { color: 'green', pill: true })"
        );
        var badgeJson = RenderNodeJsonSerializer.Serialize(badge.Body);
        Assert.Equal("span", (string?)badgeJson["tag"]);
        Assert.Equal("badge bg-green-lt badge-pill", (string?)badgeJson["attributes"]!["class"]);

        var alert = EvaluateDisplayContent(
            session,
            "ui.alert('Saved', { variant: 'success', title: 'Done' })"
        );
        var alertJson = RenderNodeJsonSerializer.Serialize(alert.Body);
        Assert.Equal("alert alert-success", (string?)alertJson["attributes"]!["class"]);
        Assert.Equal("Done", (string?)alertJson["children"]![0]!["children"]![0]!["value"]);

        var spinner = EvaluateDisplayContent(session, "ui.spinner({ color: 'blue', small: true })");
        var spinnerJson = RenderNodeJsonSerializer.Serialize(spinner.Body);
        Assert.Equal(
            "spinner-border text-blue spinner-border-sm",
            (string?)spinnerJson["attributes"]!["class"]
        );

        var status = EvaluateDisplayContent(
            session,
            "ui.status('Online', { color: 'green', animated: true })"
        );
        var statusJson = RenderNodeJsonSerializer.Serialize(status.Body);
        Assert.Equal("status status-green", (string?)statusJson["attributes"]!["class"]);
        Assert.Equal(
            "status-dot status-dot-animated",
            (string?)statusJson["children"]![0]!["attributes"]!["class"]
        );

        var icon = EvaluateDisplayContent(
            session,
            "ui.icon('alert-triangle', { size: 24, color: 'warning' })"
        );
        var iconJson = RenderNodeJsonSerializer.Serialize(icon.Body);
        Assert.Equal("i", (string?)iconJson["tag"]);
        Assert.Equal(
            "ti ti-alert-triangle text-warning",
            (string?)iconJson["attributes"]!["class"]
        );
        Assert.Equal("font-size: 24px", (string?)iconJson["attributes"]!["style"]);

        var progress = EvaluateDisplayContent(
            session,
            "ui.progress(75, { color: 'green', label: '75%' })"
        );
        var progressJson = RenderNodeJsonSerializer.Serialize(progress.Body);
        var bar = progressJson["children"]![0];
        Assert.Equal("progress-bar bg-green", (string?)bar!["attributes"]!["class"]);
        Assert.Equal("width: 75%", (string?)bar["attributes"]!["style"]);
        Assert.Equal("75", (string?)bar["attributes"]!["aria-valuenow"]);
        Assert.Equal("75%", (string?)bar["children"]![0]!["value"]);
    }

    [Fact]
    public async Task Table_from_js_serializes_correctly()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
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
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        Assert.ThrowsAny<Exception>(() => session.Evaluate("ui.element('script')"));
    }

    [Fact]
    public async Task Invalid_tabler_component_arguments_from_js_throw()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        Assert.ThrowsAny<Exception>(() => session.Evaluate("ui.progress(null)"));
        Assert.ThrowsAny<Exception>(() => session.Evaluate("ui.progress(101)"));
        Assert.ThrowsAny<Exception>(() =>
            session.Evaluate("ui.alert('Saved', { variant: 'primary' })")
        );
    }

    [Fact]
    public async Task Button_from_js_returns_button_content_with_click_interaction()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var result = session.Evaluate("ui.button('Run', () => {})");
        var content = Assert.IsAssignableFrom<DisplayContent>(result.ToObject());

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("button", (string?)json["tag"]);
        Assert.Equal("Run", (string?)json["children"]![0]!["value"]);

        var interaction = Assert.Single(content.Interactions);
        Assert.Equal(InteractionEvent.Click, interaction.Event);
    }

    private static DisplayContent EvaluateDisplayContent(DuetsSession session, string source)
    {
        var result = session.Evaluate(source);
        return Assert.IsAssignableFrom<DisplayContent>(result.ToObject());
    }
}
