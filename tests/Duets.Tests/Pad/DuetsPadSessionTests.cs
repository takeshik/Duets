using System.Threading.Channels;
using Duets.Jint;
using Duets.Pad;
using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Jint;

namespace Duets.Tests.Pad;

/// <summary>
/// Integration tests for <see cref="DuetsPadSession"/> using a real Jint-backed
/// <see cref="DuetsSession"/>.
/// </summary>
public sealed class DuetsPadSessionTests
{
    private static async Task<DuetsPadSession> CreatePadSessionAsync()
    {
        var duetsSession = await DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));
        return new DuetsPadSession(Guid.NewGuid(), duetsSession);
    }

    // -------------------------------------------------------------------------
    // dump()
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dump_string_returns_ok_and_appends_timeline_entry()
    {
        using var session = await CreatePadSessionAsync();

        var result = await session.EvaluateAsync("""dump("x")""");

        Assert.True(result.Ok);
        Assert.Equal("x", result.Result);

        var entry = Assert.Single(session.Timeline);
        Assert.Equal("dump", entry.Reason);

        // Body should be a Text node with value "x"
        var body = Assert.IsType<Text>(entry.Body);
        Assert.Equal("x", body.Value);
    }

    [Fact]
    public async Task Dump_number_expression_returns_result_and_body()
    {
        using var session = await CreatePadSessionAsync();

        var result = await session.EvaluateAsync("dump(1 + 2)");

        Assert.True(result.Ok);
        Assert.Equal("3", result.Result);

        var entry = Assert.Single(session.Timeline);
        // The body is rendered from the number 3 — DefaultObjectRenderer produces a Text node.
        var body = Assert.IsType<Text>(entry.Body);
        Assert.Equal("3", body.Value);
    }

    [Fact]
    public async Task Dump_object_preserves_js_identity()
    {
        using var session = await CreatePadSessionAsync();

        // dump returns the original JS value, so accessing .a on the result should yield 5.
        var result = await session.EvaluateAsync("dump({ a: 5 }).a");

        Assert.True(result.Ok);
        Assert.Equal("5", result.Result);
    }

    [Fact]
    public async Task Dump_appends_exactly_once_per_call()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""dump("first")""");
        await session.EvaluateAsync("""dump("second")""");

        Assert.Equal(2, session.Timeline.Count);
    }

    // -------------------------------------------------------------------------
    // console.log
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Console_log_appends_timeline_entry_with_console_reason()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""console.log("hello")""");

        var entry = Assert.Single(session.Timeline);
        Assert.Equal("console", entry.Reason);

        var body = Assert.IsType<Element>(entry.Body);
        Assert.Contains("duetspad-console", body.Attributes.First(a => a.Key == "class").Value);
        Assert.Contains("duetspad-console-log", body.Attributes.First(a => a.Key == "class").Value);

        // The child should be a Text node with the message.
        var child = Assert.IsType<Text>(body.Children[0]);
        Assert.Equal("hello", child.Value);
    }

    [Fact]
    public async Task Console_log_with_appendResult_true_produces_only_console_entry_no_evaluation_entry()
    {
        using var session = await CreatePadSessionAsync();

        // console.log returns null in the Jint backend (a void operation).
        // With appendResult: true, the Null result must be silently skipped — only the console
        // entry produced by the side-effect handler should appear.
        await session.EvaluateAsync("""console.log("x")""", appendResult: true);

        var entry = Assert.Single(session.Timeline);
        Assert.Equal("console", entry.Reason);
        // No spurious "evaluation" entry.
        Assert.DoesNotContain(session.Timeline, e => e.Reason == "evaluation");
    }

    // -------------------------------------------------------------------------
    // canvas.add / canvas.set / canvas.clear
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Canvas_add_appends_one_child_to_canvas()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""canvas.add(ui.label("hi"))""");

        Assert.Single(session.Canvas.Root.Children);
    }

    [Fact]
    public async Task Canvas_set_replaces_all_children()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""canvas.add(ui.label("a"))""");
        await session.EvaluateAsync("""canvas.add(ui.label("b"))""");
        Assert.Equal(2, session.Canvas.Root.Children.Count);

        await session.EvaluateAsync("""canvas.set(ui.rawHtml("<b>x</b>"))""");
        Assert.Single(session.Canvas.Root.Children);
    }

    [Fact]
    public async Task Canvas_clear_empties_canvas()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""canvas.add(ui.label("hi"))""");
        Assert.Single(session.Canvas.Root.Children);

        await session.EvaluateAsync("canvas.clear()");
        Assert.Empty(session.Canvas.Root.Children);
    }

    // -------------------------------------------------------------------------
    // Render failure: no exception escapes, output-error marker appended
    // -------------------------------------------------------------------------

    private sealed class ThrowingRenderer(object sentinel) : IObjectRenderer
    {
        private readonly object _sentinel = sentinel;

        public bool CanRender(object? value) => ReferenceEquals(value, this._sentinel);

        public IRenderNode Render(object? value) =>
            throw new InvalidOperationException("deliberate render failure");
    }

    [Fact]
    public async Task Dump_render_failure_appends_output_error_marker_and_does_not_throw()
    {
        using var session = await CreatePadSessionAsync();
        var sentinel = new object();
        session.SetObjectRenderers([new ThrowingRenderer(sentinel)]);

        // Call the internal op directly with the sentinel CLR value.
        var exception = Record.Exception(() => session.Dump(sentinel));

        Assert.Null(exception);
        var entry = Assert.Single(session.Timeline);

        var body = Assert.IsType<Element>(entry.Body);
        var classAttr = body.Attributes.First(a => a.Key == "class").Value;
        Assert.Equal("duetspad-output-error", classAttr);
    }

    [Fact]
    public async Task CanvasAdd_render_failure_leaves_canvas_unchanged_and_does_not_throw()
    {
        using var session = await CreatePadSessionAsync();
        var sentinel = new object();
        session.SetObjectRenderers([new ThrowingRenderer(sentinel)]);

        var canvasBefore = session.Canvas;
        var exception = Record.Exception(() => session.CanvasAdd(sentinel));

        Assert.Null(exception);
        // Canvas must be unchanged.
        Assert.Equal(canvasBefore, session.Canvas);
        // A render-error Timeline entry should have been appended.
        var entry = Assert.Single(session.Timeline);
        Assert.Equal("render-error", entry.Reason);
    }

    // -------------------------------------------------------------------------
    // d.ts declarations
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Declarations_contain_canvas_and_ui_but_not_dump_redefinition()
    {
        using var session = await CreatePadSessionAsync();

        var declarations = session.DuetsSession.Declarations.GetDeclarations();

        // There must be at least one declaration mentioning canvas and ui.
        var allContent = string.Join("\n", declarations.Select(d => d.Content));
        Assert.Contains("canvas", allContent, StringComparison.Ordinal);
        Assert.Contains("ui", allContent, StringComparison.Ordinal);

        // The per-session declaration file is the one that declares canvas (injected in the ctor).
        // It must NOT contain a dump redeclaration (core ScriptEngineInit.d.ts already does).
        var perSessionDecl = declarations
            .Where(d => d.FileName.StartsWith("decl-", StringComparison.Ordinal))
            .SingleOrDefault(d =>
                d.Content.Contains("canvas", StringComparison.Ordinal)
                && d.Content.Contains("ui", StringComparison.Ordinal)
            );
        Assert.NotNull(perSessionDecl);
        Assert.DoesNotContain(
            "declare const dump",
            perSessionDecl!.Content,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "declare function dump",
            perSessionDecl.Content,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("declare var dump", perSessionDecl.Content, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // EvaluateAsync appendResult — Immediate path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EvaluateAsync_appendResult_true_appends_evaluation_entry_to_timeline()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("1 + 2", appendResult: true);

        var entry = Assert.Single(session.Timeline);
        Assert.Equal("evaluation", entry.Reason);

        var body = Assert.IsType<Text>(entry.Body);
        Assert.Equal("3", body.Value);
    }

    [Fact]
    public async Task EvaluateAsync_appendResult_false_does_not_append_evaluation_entry()
    {
        using var session = await CreatePadSessionAsync();

        // Default (Editor path) — no evaluation entry.
        await session.EvaluateAsync("1 + 2");

        Assert.Empty(session.Timeline);
    }

    [Fact]
    public async Task EvaluateAsync_dump_without_appendResult_produces_exactly_one_dump_entry()
    {
        using var session = await CreatePadSessionAsync();

        // Editor path with a dump call — only the dump entry should appear.
        await session.EvaluateAsync("""dump("x")""");

        var entry = Assert.Single(session.Timeline);
        Assert.Equal("dump", entry.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_appendResult_true_skips_undefined_result()
    {
        using var session = await CreatePadSessionAsync();

        // void 0 evaluates to undefined — no evaluation entry should be appended.
        // Note: in the Jint backend console.log returns null (not undefined), so void 0
        // is used here to reliably exercise the ScriptValue.Undefined skip path.
        await session.EvaluateAsync("void 0", appendResult: true);

        Assert.Empty(session.Timeline);
    }

    [Fact]
    public async Task EvaluateAsync_appendResult_true_skips_null_result()
    {
        using var session = await CreatePadSessionAsync();

        // The null literal evaluates to ScriptValue.Null. Void script operations
        // (console.log, canvas.add/set/clear) also surface as Null in the Jint backend,
        // so Null must be treated the same as Undefined and produce no "evaluation" entry.
        await session.EvaluateAsync("null", appendResult: true);

        Assert.Empty(session.Timeline);
    }

    [Fact]
    public async Task EvaluateAsync_appendResult_true_dump_then_eval_result_ordered()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""dump("a"); 1 + 2""", appendResult: true);

        Assert.Equal(2, session.Timeline.Count);
        Assert.Equal("dump", session.Timeline[0].Reason);
        var dumpBody = Assert.IsType<Text>(session.Timeline[0].Body);
        Assert.Equal("a", dumpBody.Value);

        Assert.Equal("evaluation", session.Timeline[1].Reason);
        var evalBody = Assert.IsType<Text>(session.Timeline[1].Body);
        Assert.Equal("3", evalBody.Value);
    }

    // -------------------------------------------------------------------------
    // Concurrency: two EvaluateAsync calls must not cause DuetsSession concurrent-use exception
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_evaluate_calls_do_not_throw_concurrent_use_exception()
    {
        using var session = await CreatePadSessionAsync();

        var t1 = session.EvaluateAsync("1 + 1");
        var t2 = session.EvaluateAsync("2 + 2");

        var results = await Task.WhenAll(t1, t2);

        Assert.All(results, r => Assert.True(r.Ok, $"Eval failed: {r.Error}"));
    }

    // -------------------------------------------------------------------------
    // SSE subscriber: initial snapshot + update
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Canvas_subscriber_receives_canvas_snapshot_on_subscribe_and_canvas_replace_after_mutation()
    {
        using var session = await CreatePadSessionAsync();

        var channel = Channel.CreateUnbounded<CanvasEventMessage>();
        session.AddCanvasSubscriber(channel.Writer);

        // First message: canvas.snapshot of empty canvas.
        var msg1 = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CanvasEventTypes.Snapshot, msg1.Type);
        Assert.Empty(msg1.State.Root.Children);

        // Trigger a canvas mutation.
        await session.EvaluateAsync("""canvas.add(ui.label("test"))""");

        // Second message: canvas.replace reflecting the addition.
        var msg2 = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CanvasEventTypes.Replace, msg2.Type);
        Assert.Single(msg2.State.Root.Children);
    }

    [Fact]
    public async Task Timeline_subscriber_receives_timeline_reset_on_subscribe_and_timeline_append_after_append()
    {
        using var session = await CreatePadSessionAsync();

        var channel = Channel.CreateUnbounded<TimelineEventMessage>();
        session.AddTimelineSubscriber(channel.Writer);

        // First message: timeline.reset of empty timeline.
        var msg1 = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TimelineEventTypes.Reset, msg1.Type);
        Assert.Equal("initial", msg1.Reason);
        Assert.NotNull(msg1.State);
        Assert.Empty(msg1.State!);

        // Trigger an append.
        await session.EvaluateAsync("""dump("hi")""");

        // Second message: timeline.append event for the new entry.
        var msg2 = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TimelineEventTypes.Append, msg2.Type);
        Assert.NotNull(msg2.Entry);
    }
}
