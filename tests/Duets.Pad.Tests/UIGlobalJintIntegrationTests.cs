using Duets.Jint;
using Duets.Pad;
using Duets.Pad.Rendering;
using Duets.Tests.TestSupport;
using Jint;

namespace Duets.Pad.Tests;

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
    public async Task Diagnostic_content_components_from_js_serialize_correctly()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var content = EvaluateDisplayContent(
            session,
            """
            ui.stack([
              ui.dataGrid([{ label: 'State', content: ui.status('Ready') }]),
              ui.emptySpace('No warnings', { message: 'All clear', icon: 'circle-check' }),
              ui.code('<unsafe>', { wrap: true }),
              ui.preformatted('line 1\n  line 2'),
              ui.disclosure('Details', ui.text('body'), { open: true })
            ])
            """
        );

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        var children = json["children"]!.AsArray();
        Assert.Equal(5, children.Count);
        Assert.Equal("datagrid", (string?)children[0]!["attributes"]!["class"]);
        Assert.Equal("empty", (string?)children[1]!["attributes"]!["class"]);
        Assert.Equal("code", (string?)children[2]!["children"]![0]!["tag"]);
        Assert.Equal("text", (string?)children[3]!["children"]![0]!["kind"]);
        Assert.Equal("details", (string?)children[4]!["tag"]);
        Assert.True(children[4]!["attributes"]!.AsObject().ContainsKey("open"));
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
    public async Task Stack_with_horizontal_direction_from_js()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var result = session.Evaluate("ui.stack(['a', 'b'], { direction: 'horizontal' })");
        var content = Assert.IsAssignableFrom<DisplayContent>(result.ToObject());

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal(
            "duetspad-stack duetspad-stack-horizontal",
            (string?)json["attributes"]!["class"]
        );
        Assert.Equal(2, json["children"]!.AsArray().Count);
    }

    [Fact]
    public async Task Card_from_js_serializes_correctly()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var result = session.Evaluate("ui.card([ui.text('hello')], { title: 'Test Card' })");
        var content = Assert.IsAssignableFrom<DisplayContent>(result.ToObject());

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("card", (string?)json["attributes"]!["class"]);
        var children = json["children"]!.AsArray();
        Assert.Equal(2, children.Count); // header + body
        Assert.Equal("card-header", (string?)children[0]!["attributes"]!["class"]);
        Assert.Equal("card-body", (string?)children[1]!["attributes"]!["class"]);
    }

    [Fact]
    public async Task Row_from_js_renders_row_div()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var content = EvaluateDisplayContent(session, "ui.row([ui.text('hello')])");

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("row", (string?)json["attributes"]!["class"]);
        Assert.Single(json["children"]!.AsArray());
    }

    [Fact]
    public async Task Col_with_no_options_from_js_renders_auto_col()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var content = EvaluateDisplayContent(session, "ui.col()");

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("col", (string?)json["attributes"]!["class"]);
    }

    [Fact]
    public async Task Col_with_span_option_from_js()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var content = EvaluateDisplayContent(session, "ui.col(null, { span: 6 })");

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("col-6", (string?)json["attributes"]!["class"]);
    }

    [Fact]
    public async Task Divider_with_no_options_from_js_renders_hr()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var content = EvaluateDisplayContent(session, "ui.divider()");

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("hr", (string?)json["tag"]);
    }

    [Fact]
    public async Task Divider_with_text_option_from_js()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var content = EvaluateDisplayContent(session, "ui.divider({ text: 'Section' })");

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("div", (string?)json["tag"]);
        Assert.Equal("hr-text", (string?)json["attributes"]!["class"]);
        Assert.Equal("Section", (string?)json["children"]![0]!["value"]);
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

    [Fact]
    public async Task Link_with_url_from_js_serializes_correctly()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var result = session.Evaluate("ui.link('Visit', 'https://example.com')");
        var content = Assert.IsAssignableFrom<DisplayContent>(result.ToObject());

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("a", (string?)json["tag"]);
        Assert.Equal("https://example.com", (string?)json["attributes"]!["href"]);
        Assert.Equal("_blank", (string?)json["attributes"]!["target"]);
        Assert.Equal("Visit", (string?)json["children"]![0]!["value"]);
        Assert.Empty(content.Interactions);
    }

    [Fact]
    public async Task Link_with_handler_from_js_returns_click_interaction()
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        var result = session.Evaluate("ui.link('Run', () => {})");
        var content = Assert.IsAssignableFrom<DisplayContent>(result.ToObject());

        var json = RenderNodeJsonSerializer.Serialize(content.Body);
        Assert.Equal("a", (string?)json["tag"]);
        Assert.Equal("button", (string?)json["attributes"]!["role"]);
        Assert.Equal("Run", (string?)json["children"]![0]!["value"]);

        var interaction = Assert.Single(content.Interactions);
        Assert.Equal(InteractionEvent.Click, interaction.Event);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public async Task Link_with_unsafe_url_scheme_from_js_throws(string url)
    {
        using var session = await CreateSessionAsync();
        var ui = new UIGlobal(new DisplayRenderer([]), DumpOptions.Default);
        session.SetValue("ui", ui);

        Assert.ThrowsAny<Exception>(() =>
            session.Evaluate($"ui.link('xss', {ToJsStringLiteral(url)})")
        );
    }

    private static string ToJsStringLiteral(string value) =>
        "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private static DisplayContent EvaluateDisplayContent(DuetsSession session, string source)
    {
        var result = session.Evaluate(source);
        return Assert.IsAssignableFrom<DisplayContent>(result.ToObject());
    }
}
