using System.Threading.Channels;
using Duets;
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

    private static async Task<DuetsPadSession> CreatePadSessionAsync(
        IReadOnlyList<IObjectRenderer> renderers
    )
    {
        var duetsSession = await DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));
        return new DuetsPadSession(Guid.NewGuid(), duetsSession, renderers);
    }

    private static IReadOnlyList<Element> AssertScalarTableRows(IRenderNode result)
    {
        var table = Assert.IsType<Element>(result);
        Assert.Equal("table", table.Tag);
        Assert.Equal("duetspad-table", table.Attributes["class"]);
        Assert.Equal(2, table.Children.Count);

        var thead = Assert.IsType<Element>(table.Children[0]);
        Assert.Equal("thead", thead.Tag);
        Assert.Single(thead.Children);

        var tbody = Assert.IsType<Element>(table.Children[1]);
        Assert.Equal("tbody", tbody.Tag);
        return [.. tbody.Children.Select(Assert.IsType<Element>)];
    }

    private static ITerminalRenderNode GetScalarCellValue(Element row)
    {
        var td = Assert.IsType<Element>(Assert.Single(row.Children));
        Assert.Equal("td", td.Tag);
        return Assert.Single(td.Children);
    }

    /// <summary>
    /// An object renderer that blocks inside <see cref="Render"/> until released. Used to hold an
    /// eval open at a deterministic point (the render is driven synchronously from the eval thread
    /// via <c>dump()</c>), so a test can call <see cref="DuetsPadSession.Dispose"/> on another
    /// thread while the eval — and therefore the eval semaphore — is provably still in flight.
    /// This avoids any reliance on timing or a busy loop.
    /// </summary>
    private sealed class BlockingRenderer : IObjectRenderer
    {
        private readonly ManualResetEventSlim _release = new(false);

        /// <summary>Signals once <see cref="Render"/> has been entered (eval is mid-flight).</summary>
        public ManualResetEventSlim Entered { get; } = new(false);

        /// <summary>Allows the blocked <see cref="Render"/> call to complete.</summary>
        public void Release() => this._release.Set();

        public bool CanRender(object value) => value is "block";

        public IRenderNode Render(object value, RenderContext context)
        {
            this.Entered.Set();
            this._release.Wait();
            return new Text("blocked");
        }
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

    // ── dump per-call options override ────────────────────────────────────────

    [Fact]
    public async Task Session_DumpOptions_property_reflects_value_passed_to_constructor()
    {
        var duetsSession = await DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));
        var customOptions = new DumpOptions { MaxDepth = 7, MaxItems = 42 };
        using var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            dumpOptions: customOptions
        );

        Assert.Equal(7, session.DumpOptions.MaxDepth);
        Assert.Equal(42, session.DumpOptions.MaxItems);
    }

    [Fact]
    public async Task Dump_per_call_maxDepth_override_truncates_nested_value()
    {
        // With maxDepth=1, a list-of-lists should be truncated at depth 1.
        using var session = await CreatePadSessionAsync();

        // Build a nested structure in JS: [[1, 2], [3, 4]]
        // With maxDepth=1 the inner arrays are at depth 1 (>=1) and should be truncated to "[…]".
        var result = await session.EvaluateAsync("dump([[1, 2], [3, 4]], { maxDepth: 1 })");

        Assert.True(result.Ok);

        var entry = Assert.Single(session.Timeline);
        Assert.Equal("dump", entry.Reason);

        // The outer array is rendered at depth 0; inner arrays at depth 1 should be "[…]".
        var rows = AssertScalarTableRows(entry.Body);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new Text("[…]"), GetScalarCellValue(rows[0]));
        Assert.Equal(new Text("[…]"), GetScalarCellValue(rows[1]));
    }

    [Fact]
    public async Task Dump_per_call_maxDepth_does_not_affect_subsequent_dump_call()
    {
        // The per-call override must not persist to the next dump call.
        using var session = await CreatePadSessionAsync();

        // First dump with maxDepth=1 — inner arrays truncated.
        await session.EvaluateAsync("dump([[1, 2]], { maxDepth: 1 })");

        // Second dump with no override — session default (MaxDepth=5) applies, inner array renders.
        await session.EvaluateAsync("dump([[1, 2]])");

        Assert.Equal(2, session.Timeline.Count);

        var first = session.Timeline[0];
        var second = session.Timeline[1];

        var firstRows = AssertScalarTableRows(first.Body);
        Assert.Equal(new Text("[…]"), GetScalarCellValue(Assert.Single(firstRows)));

        var secondRows = AssertScalarTableRows(second.Body);
        var secondInner = Assert.IsType<Element>(GetScalarCellValue(Assert.Single(secondRows)));
        AssertScalarTableRows(secondInner);
    }

    [Fact]
    public async Task Dump_per_call_negative_maxItems_falls_back_to_session_default_and_does_not_throw()
    {
        // A script-supplied negative maxItems must be silently ignored — dump must succeed and the
        // session default MaxItems must be used instead of the invalid value.
        using var session = await CreatePadSessionAsync();

        // This would have thrown ArgumentOutOfRangeException before the fix.
        var result = await session.EvaluateAsync("dump([1, 2, 3], { maxItems: -1 })");

        Assert.True(result.Ok, $"dump threw: {result.Error}");
        var entry = Assert.Single(session.Timeline);
        Assert.Equal("dump", entry.Reason);

        // All 3 items must be visible — the default MaxItems (1000) applies.
        var rows = AssertScalarTableRows(entry.Body);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task Dump_per_call_negative_maxDepth_falls_back_to_session_default_and_does_not_throw()
    {
        // A script-supplied negative maxDepth must be silently ignored — dump must succeed.
        using var session = await CreatePadSessionAsync();

        var result = await session.EvaluateAsync("dump([1, 2, 3], { maxDepth: -1 })");

        Assert.True(result.Ok, $"dump threw: {result.Error}");
        Assert.Single(session.Timeline);
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

        public IRenderNode Render(object value, RenderContext context) =>
            throw new InvalidOperationException("deliberate render failure");
    }

    [Fact]
    public async Task Dump_render_failure_appends_output_error_marker_and_does_not_throw()
    {
        using var session = await CreatePadSessionAsync();
        var sentinel = new object();
        session.SetObjectRenderers([new ThrowingRenderer(sentinel)]);

        // Call the internal op directly with the sentinel CLR value.
        var exception = Record.Exception(() => session.Dump(sentinel, DumpOptions.Default));

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
    public async Task Declarations_contain_canvas_ui_and_dump_with_duetspad_options()
    {
        using var session = await CreatePadSessionAsync();

        var declarations = session.DuetsSession.Declarations.GetDeclarations();

        // There must be at least one declaration mentioning canvas, ui, and dump.
        var allContent = string.Join("\n", declarations.Select(d => d.Content));
        Assert.Contains("canvas", allContent, StringComparison.Ordinal);
        Assert.Contains("ui", allContent, StringComparison.Ordinal);
        Assert.Contains("dump", allContent, StringComparison.Ordinal);

        // The per-session declaration file is the one that declares canvas (injected in the ctor).
        // It must contain dump with DuetsPad-specific options (maxDepth/maxItems),
        // not the old core options (depth/compact).
        var perSessionDecl = declarations
            .Where(d => d.FileName.StartsWith("decl-", StringComparison.Ordinal))
            .SingleOrDefault(d =>
                d.Content.Contains("canvas", StringComparison.Ordinal)
                && d.Content.Contains("ui", StringComparison.Ordinal)
            );
        Assert.NotNull(perSessionDecl);
        Assert.Contains("declare function dump", perSessionDecl!.Content, StringComparison.Ordinal);
        Assert.Contains("maxDepth", perSessionDecl.Content, StringComparison.Ordinal);
        Assert.Contains("maxItems", perSessionDecl.Content, StringComparison.Ordinal);

        // No declaration file other than the per-session one must declare dump.
        // (Core ScriptEngineInit.d.ts no longer defines dump — DuetsPad owns it.)
        var otherDeclarationsWithDump = declarations
            .Where(d => d.FileName != perSessionDecl.FileName)
            .Where(d =>
                d.Content.Contains("declare function dump", StringComparison.Ordinal)
                || d.Content.Contains("declare const dump", StringComparison.Ordinal)
                || d.Content.Contains("declare var dump", StringComparison.Ordinal)
            )
            .ToList();
        Assert.Empty(otherDeclarationsWithDump);
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
    // Dispose: completing subscriber channels
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dispose_completes_canvas_subscriber_channel()
    {
        using var session = await CreatePadSessionAsync();

        var channel = Channel.CreateUnbounded<CanvasEventMessage>();
        session.AddCanvasSubscriber(channel.Writer);

        // Consume the initial snapshot so the channel is not already complete.
        await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);

        session.Dispose();

        // After dispose the writer must be completed, so ReadAllAsync terminates.
        var items = new List<CanvasEventMessage>();
        await foreach (
            var msg in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken)
        )
        {
            items.Add(msg);
        }

        // No additional items — the channel is complete and drained.
        Assert.Empty(items);
    }

    [Fact]
    public async Task Dispose_completes_timeline_subscriber_channel()
    {
        using var session = await CreatePadSessionAsync();

        var channel = Channel.CreateUnbounded<TimelineEventMessage>();
        session.AddTimelineSubscriber(channel.Writer);

        // Consume the initial reset.
        await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);

        session.Dispose();

        var items = new List<TimelineEventMessage>();
        await foreach (
            var msg in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken)
        )
        {
            items.Add(msg);
        }

        Assert.Empty(items);
    }

    [Fact]
    public async Task Dispose_completes_type_declaration_subscriber_channel()
    {
        using var session = await CreatePadSessionAsync();

        var channel = Channel.CreateUnbounded<TypeDeclaration?>();
        session.AddTypeDeclarationSubscriber(channel.Writer);

        session.Dispose();

        // After dispose the writer must be completed, so ReadAllAsync terminates.
        var items = new List<TypeDeclaration?>();
        await foreach (
            var decl in channel.Reader.ReadAllAsync(TestContext.Current.CancellationToken)
        )
        {
            items.Add(decl);
        }

        // Channel is complete and no items were written by this test.
        Assert.Empty(items);
    }

    [Fact]
    public async Task HasActiveSubscribers_false_initially_true_while_attached_false_after_removal()
    {
        using var session = await CreatePadSessionAsync();

        Assert.False(session.HasActiveSubscribers);

        // Canvas subscriber.
        var canvasChannel = Channel.CreateUnbounded<CanvasEventMessage>();
        var canvasKey = session.AddCanvasSubscriber(canvasChannel.Writer);
        Assert.True(session.HasActiveSubscribers);
        session.RemoveCanvasSubscriber(canvasKey);
        Assert.False(session.HasActiveSubscribers);

        // Timeline subscriber.
        var timelineChannel = Channel.CreateUnbounded<TimelineEventMessage>();
        var timelineKey = session.AddTimelineSubscriber(timelineChannel.Writer);
        Assert.True(session.HasActiveSubscribers);
        session.RemoveTimelineSubscriber(timelineKey);
        Assert.False(session.HasActiveSubscribers);

        // Type-declaration subscriber.
        var declChannel = Channel.CreateUnbounded<TypeDeclaration?>();
        var declKey = session.AddTypeDeclarationSubscriber(declChannel.Writer);
        Assert.True(session.HasActiveSubscribers);
        session.RemoveTypeDeclarationSubscriber(declKey);
        Assert.False(session.HasActiveSubscribers);
    }

    // -------------------------------------------------------------------------
    // Activity tracking: Touch() / LastActivityUtc
    // -------------------------------------------------------------------------

    private static async Task<DuetsPadSession> CreatePadSessionWithClockAsync(
        Func<DateTimeOffset> clock
    )
    {
        var duetsSession = await DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));
        return new DuetsPadSession(Guid.NewGuid(), duetsSession, clock: clock);
    }

    [Fact]
    public async Task Touch_updates_LastActivityUtc_to_current_clock_value()
    {
        var t0 = DateTimeOffset.UtcNow;
        var current = t0;
        using var session = await CreatePadSessionWithClockAsync(() => current);

        var after0 = session.LastActivityUtc;
        Assert.Equal(t0, after0);

        var t1 = t0.AddMinutes(5);
        current = t1;
        session.Touch();

        Assert.Equal(t1, session.LastActivityUtc);
    }

    [Fact]
    public async Task EvaluateAsync_updates_LastActivityUtc()
    {
        var t0 = DateTimeOffset.UtcNow;
        var current = t0;
        using var session = await CreatePadSessionWithClockAsync(() => current);

        var t1 = t0.AddMinutes(3);
        current = t1;
        await session.EvaluateAsync("1 + 1");

        Assert.Equal(t1, session.LastActivityUtc);
    }

    [Fact]
    public async Task AddCanvasSubscriber_updates_LastActivityUtc()
    {
        var t0 = DateTimeOffset.UtcNow;
        var current = t0;
        using var session = await CreatePadSessionWithClockAsync(() => current);

        var t1 = t0.AddMinutes(2);
        current = t1;

        var channel = Channel.CreateUnbounded<CanvasEventMessage>();
        session.AddCanvasSubscriber(channel.Writer);

        Assert.Equal(t1, session.LastActivityUtc);
    }

    [Fact]
    public async Task AddTimelineSubscriber_updates_LastActivityUtc()
    {
        var t0 = DateTimeOffset.UtcNow;
        var current = t0;
        using var session = await CreatePadSessionWithClockAsync(() => current);

        var t1 = t0.AddMinutes(2);
        current = t1;

        var channel = Channel.CreateUnbounded<TimelineEventMessage>();
        session.AddTimelineSubscriber(channel.Writer);

        Assert.Equal(t1, session.LastActivityUtc);
    }

    [Fact]
    public async Task EvaluateAsync_timeline_entry_timestamp_reflects_injected_clock()
    {
        var t0 = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
        using var session = await CreatePadSessionWithClockAsync(() => t0);

        await session.EvaluateAsync("""dump("hello")""");

        var entry = Assert.Single(session.Timeline);
        Assert.Equal(t0, entry.Timestamp);
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

    // -------------------------------------------------------------------------
    // Dispose / eval coordination
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dispose_while_eval_in_flight_does_not_throw_concurrent_operation()
    {
        var ct = TestContext.Current.CancellationToken;
        var renderer = new BlockingRenderer();
        var session = await CreatePadSessionAsync([renderer]);

        // Start an eval that blocks inside the renderer (driven from the eval thread via dump),
        // so the eval — and the eval semaphore — is provably still held when we call Dispose.
        var evalTask = Task.Run(() => session.EvaluateAsync("""dump("block")"""), ct);

        // Wait until the eval is provably mid-flight (renderer entered), not on a timer.
        Assert.True(renderer.Entered.Wait(TimeSpan.FromSeconds(30), ct));

        // Dispose on a separate thread. It must block on the eval semaphore (not throw), and
        // must not surface DuetsSession's concurrent-operation exception.
        var disposeTask = Task.Run(session.Dispose, ct);

        // Dispose cannot complete while the eval holds the semaphore: it is still pending here.
        await Task.Delay(200, ct);
        Assert.False(disposeTask.IsCompleted);

        // Unblock the eval; Dispose then proceeds.
        renderer.Release();

        // Dispose returns without throwing; the eval completes without an unobserved exception.
        await disposeTask;
        var result = await evalTask;
        Assert.True(result.Ok || result.Error == "Session has been disposed.");
    }

    [Fact]
    public async Task EvaluateAsync_after_dispose_returns_disposed_error()
    {
        var session = await CreatePadSessionAsync();
        session.Dispose();

        var result = await session.EvaluateAsync("1 + 1");

        Assert.False(result.Ok);
        Assert.Equal("Session has been disposed.", result.Error);
    }

    [Fact]
    public async Task AddCanvasSubscriber_after_dispose_returns_empty_and_completes_writer()
    {
        var session = await CreatePadSessionAsync();
        session.Dispose();

        var channel = Channel.CreateUnbounded<CanvasEventMessage>();
        var key = session.AddCanvasSubscriber(channel.Writer);

        Assert.Equal(Guid.Empty, key);
        Assert.True(channel.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task AddTimelineSubscriber_after_dispose_returns_empty_and_completes_writer()
    {
        var session = await CreatePadSessionAsync();
        session.Dispose();

        var channel = Channel.CreateUnbounded<TimelineEventMessage>();
        var key = session.AddTimelineSubscriber(channel.Writer);

        Assert.Equal(Guid.Empty, key);
        Assert.True(channel.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task AddTypeDeclarationSubscriber_after_dispose_returns_empty_and_completes_writer()
    {
        var session = await CreatePadSessionAsync();
        session.Dispose();

        var channel = Channel.CreateUnbounded<TypeDeclaration?>();
        var key = session.AddTypeDeclarationSubscriber(channel.Writer);

        Assert.Equal(Guid.Empty, key);
        Assert.True(channel.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task Dispose_is_idempotent()
    {
        var session = await CreatePadSessionAsync();

        session.Dispose();
        var ex = Record.Exception(session.Dispose);

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Registration / disposal serialization (TOCTOU)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registration and Dispose's subscriber complete/clear are serialized under the same
    /// <c>_stateLock</c>, so a subscriber registered concurrently with Dispose can never end up
    /// live-but-unclosed: it is either completed by Dispose's clear (registered before the clear)
    /// or self-completes after observing the disposed flag (registered after the clear). This test
    /// drives many concurrent registrations against a single Dispose and asserts the invariant —
    /// after Dispose returns, every handed-out writer's channel reader has observed completion.
    /// A fully deterministic single-interleaving test would require an intrusive production hook
    /// (e.g. a barrier inside the lock); the lock-based structure plus this stress invariant and
    /// the deterministic disposed-after-Add tests above are sufficient. This is an invariant
    /// assertion, not a timing assertion, so it is not flaky.
    /// </summary>
    [Fact]
    public async Task Concurrent_AddCanvasSubscriber_with_Dispose_never_leaves_writer_unclosed()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await CreatePadSessionAsync();

        const int subscriberCount = 32;
        var channels = Enumerable
            .Range(0, subscriberCount)
            .Select(_ => Channel.CreateUnbounded<CanvasEventMessage>())
            .ToArray();

        // Use a Barrier across dedicated threads (not thread-pool tasks) so every participant
        // reaches the registration/Dispose call at the same moment, maximizing the overlap window.
        // Dedicated threads avoid starving the thread pool with blocking barrier waits — important
        // because Dispose also blocks (on the eval semaphore), and a starved pool would mask the
        // race rather than exercise it.
        using var barrier = new Barrier(subscriberCount + 1);
        var threads = new List<Thread>(subscriberCount + 1);

        foreach (var channel in channels)
        {
            var writer = channel.Writer;
            var thread = new Thread(() =>
            {
                barrier.SignalAndWait();
                session.AddCanvasSubscriber(writer);
            })
            {
                IsBackground = true,
            };
            threads.Add(thread);
            thread.Start();
        }

        var disposeThread = new Thread(() =>
        {
            barrier.SignalAndWait();
            session.Dispose();
        })
        {
            IsBackground = true,
        };
        threads.Add(disposeThread);
        disposeThread.Start();

        foreach (var thread in threads)
        {
            thread.Join();
        }

        // Every writer is either registered-then-completed-by-Dispose or rejected-and-self-
        // completed: in both cases its channel reader must observe completion. None may remain
        // live (an open SSE stream that would outlive the disposed session). A subscriber that
        // registered before Dispose's clear has a buffered initial snapshot; for an unbounded
        // channel Completion does not resolve until that buffered item is drained, so we drain
        // each reader (exactly as the SSE route does) before awaiting completion. The bounded
        // timeout turns a regression — a writer left live and never completed — into a failure
        // rather than an indefinite hang.
        foreach (var channel in channels)
        {
            while (await channel.Reader.WaitToReadAsync(ct))
            {
                while (channel.Reader.TryRead(out _)) { }
            }

            await channel.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
    }

    // -------------------------------------------------------------------------
    // Timeline quota / timeline.trim
    // -------------------------------------------------------------------------

    private static async Task<DuetsPadSession> CreatePadSessionWithLimitAsync(int? limit)
    {
        var duetsSession = await DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));
        return new DuetsPadSession(Guid.NewGuid(), duetsSession, timelineEntryLimit: limit);
    }

    [Fact]
    public async Task TimelineEntryLimit_zero_or_negative_throws_ArgumentOutOfRangeException()
    {
        var duetsSession = await DuetsSession.CreateAsync(c => c.UseJint(o => o.AllowClr()));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DuetsPadSession(Guid.NewGuid(), duetsSession, timelineEntryLimit: 0)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DuetsPadSession(Guid.NewGuid(), duetsSession, timelineEntryLimit: -1)
        );

        duetsSession.Dispose();
    }

    [Fact]
    public async Task Unlimited_default_never_trims_and_never_emits_timeline_trim()
    {
        using var session = await CreatePadSessionAsync();

        var channel = Channel.CreateUnbounded<TimelineEventMessage>();
        session.AddTimelineSubscriber(channel.Writer);
        // Drain the initial reset.
        channel.Reader.TryRead(out _);

        // Append several entries via dump.
        const int appendCount = 10;
        for (var i = 0; i < appendCount; i++)
        {
            await session.EvaluateAsync($"""dump("{i}")""");
        }

        Assert.Equal(appendCount, session.Timeline.Count);

        // Drain all events; none should be timeline.trim.
        var events = new List<TimelineEventMessage>();
        while (channel.Reader.TryRead(out var msg))
        {
            events.Add(msg);
        }

        Assert.Equal(appendCount, events.Count);
        Assert.All(events, e => Assert.Equal(TimelineEventTypes.Append, e.Type));
    }

    [Fact]
    public async Task Exceeding_limit_drops_oldest_entries_and_keeps_most_recent()
    {
        const int limit = 3;
        using var session = await CreatePadSessionWithLimitAsync(limit);

        // Append more entries than the limit.
        for (var i = 0; i < 7; i++)
        {
            await session.EvaluateAsync($"""dump("{i}")""");
        }

        Assert.Equal(limit, session.Timeline.Count);
        // The most recent 3 entries (ids 4, 5, 6) are retained; the oldest are gone.
        Assert.Equal(4L, session.Timeline[0].Id);
        Assert.Equal(5L, session.Timeline[1].Id);
        Assert.Equal(6L, session.Timeline[2].Id);
    }

    [Fact]
    public async Task Live_subscriber_receives_timeline_trim_event_with_correct_removeBeforeId()
    {
        const int limit = 3;
        using var session = await CreatePadSessionWithLimitAsync(limit);

        var channel = Channel.CreateUnbounded<TimelineEventMessage>();
        session.AddTimelineSubscriber(channel.Writer);
        // Drain initial reset.
        channel.Reader.TryRead(out _);

        // Append exactly limit entries — no trim yet.
        for (var i = 0; i < limit; i++)
        {
            await session.EvaluateAsync($"""dump("{i}")""");
        }

        // The (limit+1)-th append triggers the first trim.
        await session.EvaluateAsync($"""dump("trigger")""");

        // Collect all events for this fourth append.
        var events = new List<TimelineEventMessage>();
        while (channel.Reader.TryRead(out var msg))
        {
            events.Add(msg);
        }

        // Expect: 3 appends (ids 0,1,2) + then append id=3 + trim removing id 0.
        var trimEvents = events.Where(e => e.Type == TimelineEventTypes.Trim).ToList();
        Assert.NotEmpty(trimEvents);

        var trim = trimEvents[^1];
        // removeBeforeId == 1: entries with id < 1 (i.e. id 0) are removed; ids 1,2,3 retained.
        Assert.Equal(1L, trim.RemoveBeforeId);
    }

    [Fact]
    public async Task Subscriber_sees_append_before_trim_for_same_eval()
    {
        const int limit = 2;
        using var session = await CreatePadSessionWithLimitAsync(limit);

        var channel = Channel.CreateUnbounded<TimelineEventMessage>();
        session.AddTimelineSubscriber(channel.Writer);
        // Drain initial reset.
        channel.Reader.TryRead(out _);

        // Fill to limit.
        for (var i = 0; i < limit; i++)
        {
            await session.EvaluateAsync($"""dump("{i}")""");
        }

        // Drain the limit-filling appends.
        while (channel.Reader.TryRead(out _)) { }

        // This append triggers a trim.
        await session.EvaluateAsync("""dump("trigger")""");

        // Collect events from the triggering eval.
        var events = new List<TimelineEventMessage>();
        while (channel.Reader.TryRead(out var msg))
        {
            events.Add(msg);
        }

        Assert.True(events.Count >= 2, "Expected at least one append and one trim event.");
        var appendIdx = events.FindIndex(e => e.Type == TimelineEventTypes.Append);
        var trimIdx = events.FindIndex(e => e.Type == TimelineEventTypes.Trim);
        Assert.True(
            appendIdx >= 0 && trimIdx > appendIdx,
            "timeline.append must appear before timeline.trim"
        );
    }

    [Fact]
    public async Task Entry_ids_are_not_reused_after_trim()
    {
        const int limit = 2;
        using var session = await CreatePadSessionWithLimitAsync(limit);

        // Append enough to force a trim.
        for (var i = 0; i < limit + 1; i++)
        {
            await session.EvaluateAsync($"""dump("{i}")""");
        }

        // At this point Timeline has 2 entries; NextId must be limit+1 (= 3), not reset.
        Assert.Equal(limit + 1, (int)session.Timeline.NextId);

        // A further append uses the next id (= 3), not 0 or 1.
        await session.EvaluateAsync("""dump("after-trim")""");

        var lastId = session.Timeline[^1].Id;
        Assert.Equal(limit + 1L, lastId);
    }

    [Fact]
    public async Task Late_subscriber_receives_trimmed_state_via_timeline_reset()
    {
        const int limit = 2;
        using var session = await CreatePadSessionWithLimitAsync(limit);

        // Cause a trim by appending beyond the limit.
        for (var i = 0; i < limit + 2; i++)
        {
            await session.EvaluateAsync($"""dump("{i}")""");
        }

        Assert.Equal(limit, session.Timeline.Count);

        // A subscriber attaching now should receive a reset reflecting only the trimmed entries.
        var channel = Channel.CreateUnbounded<TimelineEventMessage>();
        session.AddTimelineSubscriber(channel.Writer);

        var reset = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TimelineEventTypes.Reset, reset.Type);
        Assert.NotNull(reset.State);
        Assert.Equal(limit, reset.State!.Count);

        // The reset state must NOT include entries that were trimmed.
        var lowestRetainedId = session.Timeline[0].Id;
        Assert.All(reset.State!, e => Assert.True(e.Id >= lowestRetainedId));
    }
}
