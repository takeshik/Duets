using System.Threading.Channels;
using Duets.Pad;
using Duets.Pad.Interactions;
using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Duets.Tests.TestSupport;
using Jint;

namespace Duets.Pad.Tests;

/// <summary>
/// Integration tests for <c>ui.slot</c> (the mutable <see cref="DisplaySlot"/> handle) over a real
/// Jint-backed <see cref="DuetsPadSession"/>: in-place Canvas update, interaction rebasing inside a
/// slot, and Timeline update.
/// </summary>
public sealed class DuetsPadSlotTests
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

    private static Element AssertSlotMarker(ITerminalRenderNode node)
    {
        var marker = Assert.IsType<Element>(node);
        Assert.Equal("div", marker.Tag);
        Assert.True(marker.Attributes.ContainsKey(SlotMarker.AttributeName));
        return marker;
    }

    [Fact]
    public async Task Slot_renders_as_marker_wrapping_its_initial_content()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""var s = ui.slot("loading..."); canvas.add(s);""");

        var marker = AssertSlotMarker(session.Canvas.State.Root.Children.Single());
        var text = Assert.IsType<Text>(marker.Children.Single());
        Assert.Equal("loading...", text.Value);
    }

    [Fact]
    public async Task Reassigning_content_replaces_the_marked_subtree_in_place()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync("""var s = ui.slot("loading..."); canvas.add(s);""");
        await session.EvaluateAsync("""s.content = "done";""");

        // Still a single top-level child: the same marker, with replaced content.
        var marker = AssertSlotMarker(session.Canvas.State.Root.Children.Single());
        var text = Assert.IsType<Text>(marker.Children.Single());
        Assert.Equal("done", text.Value);
    }

    [Fact]
    public async Task Reassigning_content_broadcasts_a_canvas_patch()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync("""var s = ui.slot("a"); canvas.add(s);""");
        var add = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync("""s.content = "b";""");
        var update = await ReadCanvasEventAsync(
            channel.Reader,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CanvasEventTypes.Patch, update.Type);
        Assert.Equal(add.Revision, update.BaseRevision);
        Assert.Equal(add.Revision + 1, update.Revision);
    }

    [Fact]
    public async Task Interaction_inside_slot_content_is_rebased_and_invokable_after_update()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync("""var s = ui.slot("idle"); canvas.add(s);""");
        _ = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync("""s.content = ui.button("Run", () => dump("clicked"));""");
        var update = await ReadCanvasEventAsync(
            channel.Reader,
            TestContext.Current.CancellationToken
        );

        var interaction = Assert.Single(update.Interactions);
        // Marker is canvas child [0]; the slot content (button) sits at the marker's child [0].
        Assert.Equal([0, 0], interaction.Target.Segments);
        Assert.Equal(InteractionEvent.Click, interaction.Event);
        Assert.Equal(InteractionState.Live, interaction.State);

        var invoke = await session.InvokeInteractionAsync(interaction.HandlerId);

        Assert.True(invoke.Ok, invoke.Error);
        var entry = Assert.Single(session.Timeline.State);
        Assert.Equal("dump", entry.Reason);
        Assert.Equal("clicked", Assert.IsType<Text>(entry.Body).Value);
    }

    [Fact]
    public async Task Slot_placed_on_timeline_updates_in_place_via_timeline_update()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadTimelineEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync("""var s = ui.slot("a"); dump(s);""");
        var append = await ReadTimelineEventAsync(
            channel.Reader,
            TestContext.Current.CancellationToken
        );
        var appendEntry = Assert.IsType<AppendMessage>(append).Entry;

        await session.EvaluateAsync("""s.content = "b";""");
        var update = await ReadTimelineEventAsync(
            channel.Reader,
            TestContext.Current.CancellationToken
        );
        var updateMessage = Assert.IsType<UpdateMessage>(update);

        Assert.Equal(appendEntry.Id, updateMessage.Entry.Id);
        var marker = AssertSlotMarker(updateMessage.Entry.Body);
        Assert.Equal("b", Assert.IsType<Text>(marker.Children.Single()).Value);
    }

    [Fact]
    public async Task Unplaced_slot_assignment_records_value_without_projecting()
    {
        using var session = await CreatePadSessionAsync();

        // Assign before placing the slot anywhere.
        await session.EvaluateAsync("""var s = ui.slot("a"); s.content = "b";""");

        // Nothing is projected and no Timeline entry (not even a render marker/error) is produced.
        Assert.Empty(session.Canvas.State.Root.Children);
        Assert.Empty(session.Timeline.State);

        // The value was recorded on the handle; placing it now reflects the latest value.
        await session.EvaluateAsync("canvas.add(s);");
        var marker = AssertSlotMarker(session.Canvas.State.Root.Children.Single());
        Assert.Equal("b", Assert.IsType<Text>(marker.Children.Single()).Value);
    }

    [Fact]
    public async Task Reassigning_identical_content_does_not_advance_the_revision()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        _ = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        await session.EvaluateAsync("""var s = ui.slot("x"); canvas.add(s);""");
        var add = await ReadCanvasEventAsync(channel.Reader, TestContext.Current.CancellationToken);

        // The identical reassignment is a no-op (emits nothing); the subsequent real change must
        // therefore base off the add revision, proving the no-op did not advance it.
        await session.EvaluateAsync("""s.content = "x";""");
        await session.EvaluateAsync("""s.content = "y";""");
        var change = await ReadCanvasEventAsync(
            channel.Reader,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(CanvasEventTypes.Patch, change.Type);
        Assert.Equal(add.Revision, change.BaseRevision);
        Assert.Equal(add.Revision + 1, change.Revision);
    }

    [Fact]
    public async Task Slot_placed_in_multiple_canvases_updates_every_placement()
    {
        using var session = await CreatePadSessionAsync();

        await session.EvaluateAsync(
            """var s = ui.slot("a"); canvas.add(s); canvases.get("other").add(s);"""
        );
        await session.EvaluateAsync("""s.content = "b";""");

        var defaultMarker = AssertSlotMarker(session.Canvas.State.Root.Children.Single());
        Assert.Equal("b", Assert.IsType<Text>(defaultMarker.Children.Single()).Value);

        Assert.True(session.TryGetCanvasSnapshot("other", out var other));
        var otherMarker = AssertSlotMarker(other.State.Root.Children.Single());
        Assert.Equal("b", Assert.IsType<Text>(otherMarker.Children.Single()).Value);
    }
}
