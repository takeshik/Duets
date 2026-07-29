using System.Threading.Channels;
using Duets.Pad;
using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Duets.Tests.TestSupport;
using Jint;

namespace Duets.Pad.Tests;

/// <summary>
/// Integration tests for the <c>ui.*</c> form-input factories (the <see cref="DisplayInput"/>
/// handle family, ADR-47) over a real Jint-backed <see cref="DuetsPadSession"/>: rendering,
/// field-store read/write, write-back projection, snapshot merge before an invoke, and lifetime
/// tied to rendered content.
/// </summary>
public sealed class DuetsPadFieldTests
{
    private static async Task<DuetsPadSession> CreatePadSessionAsync()
    {
        var duetsSession = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
        return new DuetsPadSession(Guid.NewGuid(), duetsSession);
    }

    private static async Task<CanvasEventMessage> ReadCanvasEventAsync(
        ChannelReader<PadEventMessage?> reader,
        CancellationToken ct
    )
    {
        while (true)
        {
            var msg = await reader.ReadAsync(ct);
            if (msg is PadEventMessage.Canvas canvas)
            {
                return canvas.Message;
            }
        }
    }

    private static async Task<TimelineEventMessage> ReadTimelineEventAsync(
        ChannelReader<PadEventMessage?> reader,
        CancellationToken ct
    )
    {
        while (true)
        {
            var msg = await reader.ReadAsync(ct);
            if (msg is PadEventMessage.Timeline timeline)
            {
                return timeline.Message;
            }
        }
    }

    private static Element SingleChild(DuetsPadSession session) =>
        Assert.IsType<Element>(session.Canvas.State.Root.Children.Single());

    [Theory]
    [InlineData("textBox", "ui.textBox({ className: 'custom' })")]
    [InlineData("textArea", "ui.textArea({ className: 'custom' })")]
    [InlineData("numberBox", "ui.numberBox({ className: 'custom' })")]
    [InlineData("checkBox", "ui.checkBox({ className: 'custom' })")]
    [InlineData("dropDown", "ui.dropDown([], { className: 'custom' })")]
    [InlineData("slider", "ui.slider({ className: 'custom' })")]
    [InlineData("radioGroup", "ui.radioGroup([], { className: 'custom' })")]
    [InlineData("filePicker", "ui.filePicker({ className: 'custom' })")]
    public async Task Input_factories_reject_removed_class_name(string component, string code)
    {
        using var session = await CreatePadSessionAsync();

        var result = await session.EvaluateAsync(code);

        Assert.False(result.Ok);
        Assert.Contains(
            $"{component} className is not supported",
            result.Error,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task TextBox_renders_marked_input_with_current_value()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync(
            """var t = ui.textBox({ name: "n", value: "hi" }); canvas.add(t);"""
        );

        var input = SingleChild(session);
        Assert.Equal("input", input.Tag);
        Assert.Equal("text", input.Attributes["data-duetspad-field-kind"]);
        Assert.Equal("hi", input.Attributes["value"]);
        Assert.Equal("n", input.Attributes["name"]);
        Assert.Equal("form-control", input.Attributes["class"]);
        Assert.True(input.Attributes.ContainsKey("data-duetspad-field"));
    }

    [Fact]
    public async Task CheckBox_renders_checked_attribute_as_True_False_string()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""var c = ui.checkBox({ checked: true }); canvas.add(c);""");

        var wrapper = SingleChild(session);
        var input = Assert.IsType<Element>(wrapper.Children.Single());
        Assert.Equal("checkbox", input.Attributes["data-duetspad-field-kind"]);
        Assert.Equal("form-check-input", input.Attributes["class"]);
        Assert.True(input.Attributes.ContainsKey("checked"));

        var value = await session.EvaluateAsync("c.value");
        Assert.Equal("True", value.Result);
    }

    [Fact]
    public async Task DropDown_out_of_range_value_is_retained_and_not_selected()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync(
            """
            var d = ui.dropDown(["a", "b"], { value: "not-an-option" });
            canvas.add(d);
            """
        );

        var select = SingleChild(session);
        Assert.Equal("select", select.Tag);
        Assert.Equal("not-an-option", select.Attributes["value"]);
        Assert.Equal("form-select", select.Attributes["class"]);
        Assert.Equal(2, select.Children.Count);

        var value = await session.EvaluateAsync("d.value");
        Assert.Equal("not-an-option", value.Result);
    }

    [Fact]
    public async Task RadioGroup_checks_the_option_matching_the_current_value()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync(
            """
            var r = ui.radioGroup(["a", "b"], { value: "b" });
            canvas.add(r);
            """
        );

        var wrapper = SingleChild(session);
        var optionA = Assert.IsType<Element>(wrapper.Children[0]);
        var inputA = Assert.IsType<Element>(optionA.Children[0]);
        var optionB = Assert.IsType<Element>(wrapper.Children[1]);
        var inputB = Assert.IsType<Element>(optionB.Children[0]);

        Assert.False(inputA.Attributes.ContainsKey("checked"));
        Assert.True(inputB.Attributes.ContainsKey("checked"));
    }

    [Fact]
    public async Task Assigning_value_updates_the_stored_value_and_the_rendered_attribute()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""var t = ui.textBox({ name: "n" }); canvas.add(t);""");
        await session.EvaluateAsync("""t.value = "updated";""");

        var input = SingleChild(session);
        Assert.Equal("updated", input.Attributes["value"]);

        var value = await session.EvaluateAsync("t.value");
        Assert.Equal("updated", value.Result);
    }

    [Fact]
    public async Task Assigning_value_broadcasts_a_canvas_patch()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync("""var t = ui.textBox({ name: "n" }); canvas.add(t);""");
        var add = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync("""t.value = "b";""");
        var update = await ReadCanvasEventAsync(
            channel.Reader,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CanvasEventTypes.Patch, update.Type);
        Assert.Equal(add.Revision, update.BaseRevision);
        Assert.Equal(add.Revision + 1, update.Revision);
    }

    [Fact]
    public async Task Field_placed_in_multiple_canvases_updates_every_placement()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync(
            """var t = ui.textBox({ name: "n" }); canvas.add(t); canvases.get("other").add(t);"""
        );
        await session.EvaluateAsync("""t.value = "b";""");

        var defaultInput = SingleChild(session);
        Assert.Equal("b", defaultInput.Attributes["value"]);

        Assert.True(session.TryGetCanvasSnapshot("other", out var other));
        var otherInput = Assert.IsType<Element>(other.State.Root.Children.Single());
        Assert.Equal("b", otherInput.Attributes["value"]);
    }

    [Fact]
    public async Task Unplaced_field_assignment_records_value_without_projecting()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""var t = ui.textBox({ name: "n" }); t.value = "b";""");

        Assert.Empty(session.Canvas.State.Root.Children);

        await session.EvaluateAsync("canvas.add(t);");
        var input = SingleChild(session);
        Assert.Equal("b", input.Attributes["value"]);
    }

    [Fact]
    public async Task Field_value_falls_back_to_initial_value_once_its_marker_becomes_unreachable()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync(
            """var t = ui.textBox({ name: "n", value: "x" }); canvas.add(t);"""
        );

        // canvas.set replaces the canvas without t: its marker is no longer reachable from any
        // canvas or Timeline content, so PruneFieldStore removes the store entry (ADR-47
        // lifetime). The handle still falls back to its own construction-time initial value
        // rather than silently reporting "" (the same fallback that protects a field pruned
        // before it was ever placed).
        await session.EvaluateAsync("""canvas.set(ui.label("gone"));""");

        var value = await session.EvaluateAsync("t.value");
        Assert.Equal("x", value.Result);
    }

    [Fact]
    public async Task Unrelated_canvas_mutation_before_placement_does_not_lose_the_initial_value()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync(
            """
            var t = ui.textBox({ name: "n", value: "seed" });
            canvas.add(ui.label("noise"));
            canvas.add(t);
            """
        );

        var input = Assert.IsType<Element>(session.Canvas.State.Root.Children[1]);
        Assert.Equal("seed", input.Attributes["value"]);
    }

    [Fact]
    public async Task Handle_value_falls_back_to_initial_value_after_being_pruned_before_placement()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync(
            """
            var t = ui.textBox({ name: "n", value: "seed" });
            canvas.add(ui.label("noise"));
            """
        );

        // t was never placed, so the unrelated canvas.add above already pruned its store seed;
        // the handle still falls back to its own construction-time initial value.
        var value = await session.EvaluateAsync("t.value");
        Assert.Equal("seed", value.Result);
    }

    [Fact]
    public async Task CommitFieldValue_updates_canvas_state_without_broadcasting_or_advancing_revision()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync(
            """var t = ui.textBox({ name: "n", value: "a" }); canvas.add(t);"""
        );
        var add = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        var input = SingleChild(session);
        var fieldId = Guid.Parse(input.Attributes["data-duetspad-field"]!);

        await session.CommitFieldValue(fieldId, "committed");

        // No echo: the committing browser's own DOM already reflects the value it sent.
        Assert.False(channel.Reader.TryRead(out _));

        Assert.True(session.TryGetCanvasSnapshot("default", out var snapshot));
        Assert.Equal(add.Revision, snapshot.Revision);
        var committedInput = Assert.IsType<Element>(snapshot.State.Root.Children.Single());
        Assert.Equal("committed", committedInput.Attributes["value"]);
    }

    [Fact]
    public async Task CommitFieldValue_updates_checkbox_checked_state_without_broadcasting()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync("""var c = ui.checkBox({ checked: false }); canvas.add(c);""");
        var add = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        var wrapper = SingleChild(session);
        var input = Assert.IsType<Element>(wrapper.Children.Single());
        var fieldId = Guid.Parse(input.Attributes["data-duetspad-field"]!);

        await session.CommitFieldValue(fieldId, "True");

        Assert.False(channel.Reader.TryRead(out _));

        Assert.True(session.TryGetCanvasSnapshot("default", out var snapshot));
        Assert.Equal(add.Revision, snapshot.Revision);
        var committedWrapper = Assert.IsType<Element>(snapshot.State.Root.Children.Single());
        var committedInput = Assert.IsType<Element>(committedWrapper.Children.Single());
        Assert.True(committedInput.Attributes.ContainsKey("checked"));
    }

    [Fact]
    public async Task CommitFieldValue_checks_only_the_matching_radio_option_without_broadcasting()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync(
            """var r = ui.radioGroup(["a", "b"], { value: "a" }); canvas.add(r);"""
        );
        var add = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        var wrapper = SingleChild(session);
        var optionA = Assert.IsType<Element>(wrapper.Children[0]);
        var inputA = Assert.IsType<Element>(optionA.Children[0]);
        var fieldId = Guid.Parse(inputA.Attributes["data-duetspad-field"]!);

        await session.CommitFieldValue(fieldId, "b");

        Assert.False(channel.Reader.TryRead(out _));

        Assert.True(session.TryGetCanvasSnapshot("default", out var snapshot));
        Assert.Equal(add.Revision, snapshot.Revision);
        var committedWrapper = Assert.IsType<Element>(snapshot.State.Root.Children.Single());
        var committedA = Assert.IsType<Element>(
            Assert.IsType<Element>(committedWrapper.Children[0]).Children[0]
        );
        var committedB = Assert.IsType<Element>(
            Assert.IsType<Element>(committedWrapper.Children[1]).Children[0]
        );
        Assert.False(committedA.Attributes.ContainsKey("checked"));
        Assert.True(committedB.Attributes.ContainsKey("checked"));
    }

    [Fact]
    public async Task CommitFieldValue_updates_timeline_entry_body_without_broadcasting()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadTimelineEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync("""var t = ui.textBox({ name: "n", value: "a" }); dump(t);""");
        var append = await ReadTimelineEventAsync(
            channel.Reader,
            TestContext.Current.CancellationToken
        );
        var appendEntry = Assert.IsType<AppendMessage>(append).Entry;
        var input = Assert.IsType<Element>(appendEntry.Body);
        var fieldId = Guid.Parse(input.Attributes["data-duetspad-field"]!);

        await session.CommitFieldValue(fieldId, "committed");

        // No timeline.update broadcast (no echo).
        Assert.False(channel.Reader.TryRead(out _));

        var entry = Assert.Single(session.Timeline.State);
        var committedInput = Assert.IsType<Element>(entry.Body);
        Assert.Equal("committed", committedInput.Attributes["value"]);
    }

    [Fact]
    public async Task Invoke_handler_observes_a_merged_field_snapshot()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync(
            """
            var t = ui.textBox({ name: "n" });
            canvas.add(ui.stack([t, ui.button("Run", () => dump(t.value))]));
            """
        );
        var patch = await ReadCanvasEventAsync(
            channel.Reader,
            TestContext.Current.CancellationToken
        );
        var interaction = Assert.Single(patch.Interactions);

        var stack = SingleChild(session);
        var input = Assert.IsType<Element>(stack.Children[0]);
        var fieldId = Guid.Parse(input.Attributes["data-duetspad-field"]!);
        var invoke = await session.InvokeInteractionAsync(
            interaction.HandlerId,
            new Dictionary<Guid, string> { [fieldId] = "typed-not-blurred" }
        );

        Assert.True(invoke.Ok, invoke.Error);
        var entry = Assert.Single(session.Timeline.State);
        Assert.Equal("dump", entry.Reason);
        Assert.Equal("typed-not-blurred", Assert.IsType<Text>(entry.Body).Value);
    }

    [Fact]
    public async Task Invoke_field_snapshot_is_projected_into_canvas_and_timeline_state()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync(
            """
            var t = ui.textBox({ name: "n" });
            canvas.add(ui.stack([t, ui.button("Run", () => {})]));
            dump(t);
            """
        );
        var patch = await ReadCanvasEventAsync(
            channel.Reader,
            TestContext.Current.CancellationToken
        );
        var interaction = Assert.Single(patch.Interactions);

        var stack = SingleChild(session);
        var input = Assert.IsType<Element>(stack.Children[0]);
        var fieldId = Guid.Parse(input.Attributes["data-duetspad-field"]!);

        var invoke = await session.InvokeInteractionAsync(
            interaction.HandlerId,
            new Dictionary<Guid, string> { [fieldId] = "typed-not-blurred" }
        );

        Assert.True(invoke.Ok, invoke.Error);

        // The canvas and Timeline projections themselves — not only the field store the handler
        // reads — reflect the snapshot value (ADR-47 #1): the invoke merge must project like a
        // blur commit, not merely update the field store.
        Assert.True(session.TryGetCanvasSnapshot("default", out var canvasSnapshot));
        var committedStack = Assert.IsType<Element>(canvasSnapshot.State.Root.Children.Single());
        var committedInput = Assert.IsType<Element>(committedStack.Children[0]);
        Assert.Equal("typed-not-blurred", committedInput.Attributes["value"]);

        var timelineEntry = Assert.Single(session.Timeline.State);
        var timelineInput = Assert.IsType<Element>(timelineEntry.Body);
        Assert.Equal("typed-not-blurred", timelineInput.Attributes["value"]);
    }

    [Fact]
    public async Task CommitFieldValue_on_an_unreachable_field_id_does_not_revive_the_store()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync(
            """var t = ui.textBox({ name: "n", value: "x" }); canvas.add(t);"""
        );

        var input = SingleChild(session);
        var fieldId = Guid.Parse(input.Attributes["data-duetspad-field"]!);

        // canvas.set replaces the canvas without t: its marker is no longer reachable from any
        // canvas or Timeline content, so the field store entry is pruned (ADR-47 lifetime).
        await session.EvaluateAsync("""canvas.set(ui.label("gone"));""");

        // A stale, delayed blur commit for a since-removed field must be a no-op: it must not
        // revive a store entry the field's own marker no longer reaches (ADR-47 #2).
        await session.CommitFieldValue(fieldId, "revived?");

        var value = await session.EvaluateAsync("t.value");
        Assert.Equal("x", value.Result);
    }

    [Fact]
    public async Task CommitFieldValue_is_serialized_against_a_concurrent_eval()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync(
            """var t = ui.textBox({ name: "n", value: "a" }); canvas.add(t);"""
        );

        var input = SingleChild(session);
        var fieldId = Guid.Parse(input.Attributes["data-duetspad-field"]!);

        // CommitFieldValue now shares _evalSemaphore with EvaluateAsync (ADR-47 #3), so the two
        // must not deadlock or throw a concurrent-use exception, and — because "1 + 1" never
        // touches the field — the outcome must be deterministic regardless of interleaving order.
        var commitTask = session.CommitFieldValue(fieldId, "from-blur");
        var evalTask = session.EvaluateAsync("1 + 1");

        await Task.WhenAll(commitTask, evalTask);
        var evalResult = await evalTask;

        Assert.True(evalResult.Ok, evalResult.Error);
        Assert.True(session.TryGetCanvasSnapshot("default", out var snapshot));
        var committedInput = Assert.IsType<Element>(snapshot.State.Root.Children.Single());
        Assert.Equal("from-blur", committedInput.Attributes["value"]);
    }
}
