using System.Threading.Channels;
using Duets.Jint;
using Duets.Pad;
using Duets.Pad.Attachments;
using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Duets.Tests.TestSupport;
using Jint;

namespace Duets.Pad.Tests;

public sealed class DuetsPadModalTests
{
    private sealed class ThrowingRenderer(object sentinel) : IObjectRenderer
    {
        private readonly object _sentinel = sentinel;

        public bool CanRender(object? value) => ReferenceEquals(value, this._sentinel);

        public DisplayContent Render(object value, RenderContext context) =>
            throw new InvalidOperationException("deliberate modal render failure");
    }

    private static async Task<DuetsPadSession> CreatePadSessionAsync(int? maximum = 8)
    {
        var duetsSession = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
        return new DuetsPadSession(Guid.NewGuid(), duetsSession, maxActiveModals: maximum);
    }

    private static async Task<ModalEventMessage> ReadModalEventAsync(
        ChannelReader<PadEventMessage?> reader
    )
    {
        while (true)
        {
            var message = await reader.ReadAsync(TestContext.Current.CancellationToken);
            if (message is PadEventMessage.Modal modal)
            {
                return modal.Message;
            }
        }
    }

    [Fact]
    public async Task Modal_action_observes_latest_input_and_closes_once()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var evaluation = await session.EvaluateAsync(
            """
            var name = ui.textBox({ value: "initial" });
            ui.modal(
              ui.stack([ui.label("Name"), name]),
              result => dump(`${result.reason}:${result.actionId}:${name.value}`),
              {
                title: "Enter name",
                buttons: [
                  { id: "cancel", label: "Cancel" },
                  { id: "save", label: "Save", variant: "primary" }
                ],
                defaultButtonId: "save",
                dismissButtonId: "cancel"
              }
            );
            """
        );

        Assert.True(evaluation.Ok, evaluation.Error);
        var open = Assert.IsType<ModalEventMessage.FullStateMessage>(
            await ReadModalEventAsync(channel.Reader)
        );
        Assert.Equal(ModalEventTypes.Open, open.Type);
        Assert.Equal("Enter name", open.Projection.Options.Title);
        Assert.Equal(3, open.Interactions.Count);

        var input = Assert.IsType<Element>(
            open
                .Projection.State.Root.Children[0]
                .AsElement()
                .Children[0]
                .AsElement()
                .Children[0]
                .AsElement()
                .Children[1]
        );
        var fieldId = Guid.Parse(input.Attributes[FieldMarker.AttributeName]!);
        var save = open.Interactions.Single(interaction =>
            interaction.Target.Segments.SequenceEqual([0, 1, 1, 0])
        );

        var invoked = await session.InvokeInteractionAsync(
            save.HandlerId,
            new Dictionary<Guid, string> { [fieldId] = "Ada" }
        );

        Assert.True(invoked.Ok, invoked.Error);
        Assert.Equal("action:save:Ada", Assert.IsType<Text>(session.Timeline.State[^1].Body).Value);
        Assert.IsType<ModalEventMessage.CloseMessage>(await ReadModalEventAsync(channel.Reader));

        var duplicate = await session.InvokeInteractionAsync(save.HandlerId);
        Assert.True(duplicate.Stale);
    }

    [Fact]
    public async Task Active_modal_is_replayed_to_a_new_subscriber()
    {
        using var session = await CreatePadSessionAsync();
        var evaluation = await session.EvaluateAsync(
            """ui.modal("body", () => {}, { buttons: ["Close"] })"""
        );
        Assert.True(evaluation.Ok, evaluation.Error);

        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        var snapshot = Assert.IsType<ModalEventMessage.SnapshotMessage>(
            await ReadModalEventAsync(channel.Reader)
        );

        Assert.Single(snapshot.Modals);
        Assert.Equal("Close", snapshot.Modals[0].Projection.Options.Buttons[0].Id);
    }

    [Fact]
    public async Task Modal_slot_update_projects_a_new_revision()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            var modalSlot = ui.slot("before");
            ui.modal(modalSlot, () => {}, { buttons: ["Close"] });
            """
        );
        Assert.True(opened.Ok, opened.Error);
        _ = await ReadModalEventAsync(channel.Reader);

        var updated = await session.EvaluateAsync("modalSlot.content = 'after'");
        Assert.True(updated.Ok, updated.Error);
        var message = await ReadModalEventAsync(channel.Reader);

        var revision = message switch
        {
            ModalEventMessage.PatchMessage patch => patch.Revision,
            ModalEventMessage.FullStateMessage replace
                when replace.Type == ModalEventTypes.Replace => replace.Projection.Revision,
            _ => throw new InvalidOperationException("Expected a modal mutation event."),
        };
        Assert.Equal(1, revision);
    }

    [Fact]
    public async Task Modal_body_interaction_updates_content_without_closing()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            var bodySlot = ui.slot("before");
            var interactiveModal = ui.modal(
              ui.stack([
                bodySlot,
                ui.button("Update", () => bodySlot.content = "after")
              ]),
              () => {},
              { buttons: ["Close"] }
            );
            """
        );
        Assert.True(opened.Ok, opened.Error);
        var open = Assert.IsType<ModalEventMessage.FullStateMessage>(
            await ReadModalEventAsync(channel.Reader)
        );
        var update = open.Interactions.Single(interaction =>
            interaction.Target.Segments.SequenceEqual([0, 0, 0, 1])
        );

        var invoked = await session.InvokeInteractionAsync(update.HandlerId);

        Assert.True(invoked.Ok, invoked.Error);
        var mutation = await ReadModalEventAsync(channel.Reader);
        Assert.True(
            mutation
                is ModalEventMessage.PatchMessage
                    or ModalEventMessage.FullStateMessage { Type: ModalEventTypes.Replace }
        );
        var isOpen = await session.EvaluateAsync("interactiveModal.isOpen");
        Assert.True(isOpen.Ok, isOpen.Error);
        Assert.Equal("true", isOpen.Result);
    }

    [Fact]
    public async Task Modal_dismiss_reports_the_mapped_action_id()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            ui.modal(
              "body",
              result => dump(`${result.reason}:${result.actionId}`),
              { buttons: ["Cancel"], dismissButtonId: "Cancel" }
            );
            """
        );
        Assert.True(opened.Ok, opened.Error);
        var open = Assert.IsType<ModalEventMessage.FullStateMessage>(
            await ReadModalEventAsync(channel.Reader)
        );
        var dismiss = open.Interactions.Single(interaction =>
            interaction.Target.Segments.SequenceEqual([0, 2])
        );

        var invoked = await session.InvokeInteractionAsync(dismiss.HandlerId);

        Assert.True(invoked.Ok, invoked.Error);
        Assert.Equal("dismiss:Cancel", Assert.IsType<Text>(session.Timeline.State[^1].Body).Value);
        Assert.IsType<ModalEventMessage.CloseMessage>(await ReadModalEventAsync(channel.Reader));
    }

    [Fact]
    public async Task Modal_unmapped_dismiss_reports_null_action_id()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            ui.modal(
              "body",
              result => dump(`${result.reason}:${result.actionId === null}`),
              { buttons: ["Close"] }
            );
            """
        );
        Assert.True(opened.Ok, opened.Error);
        var open = Assert.IsType<ModalEventMessage.FullStateMessage>(
            await ReadModalEventAsync(channel.Reader)
        );
        var dismiss = open.Interactions.Single(interaction =>
            interaction.Target.Segments.SequenceEqual([0, 2])
        );

        var invoked = await session.InvokeInteractionAsync(dismiss.HandlerId);

        Assert.True(invoked.Ok, invoked.Error);
        Assert.Equal("dismiss:true", Assert.IsType<Text>(session.Timeline.State[^1].Body).Value);
        Assert.IsType<ModalEventMessage.CloseMessage>(await ReadModalEventAsync(channel.Reader));
    }

    [Fact]
    public async Task Multiple_modals_are_replayed_in_open_order()
    {
        using var session = await CreatePadSessionAsync();
        var opened = await session.EvaluateAsync(
            """
            var firstModal = ui.modal("first", () => {}, {
              title: "First",
              buttons: ["Close"]
            });
            var secondModal = ui.modal("second", () => {}, {
              title: "Second",
              buttons: ["Close"]
            });
            """
        );
        Assert.True(opened.Ok, opened.Error);

        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        var snapshot = Assert.IsType<ModalEventMessage.SnapshotMessage>(
            await ReadModalEventAsync(channel.Reader)
        );

        Assert.Equal(
            ["First", "Second"],
            snapshot.Modals.Select(modal => modal.Projection.Options.Title)
        );

        var closed = await session.EvaluateAsync("firstModal.close()");
        Assert.True(closed.Ok, closed.Error);
        var close = Assert.IsType<ModalEventMessage.CloseMessage>(
            await ReadModalEventAsync(channel.Reader)
        );
        Assert.Equal(snapshot.Modals[0].Projection.Id, close.ModalId);

        var reconnect = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(reconnect.Writer, session.DuetsSession.Declarations);
        var remaining = Assert.IsType<ModalEventMessage.SnapshotMessage>(
            await ReadModalEventAsync(reconnect.Reader)
        );
        Assert.Equal("Second", Assert.Single(remaining.Modals).Projection.Options.Title);
    }

    [Fact]
    public async Task Modal_file_picker_is_live_projected_and_pruned_on_close()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            var modalPicker = ui.filePicker();
            var pickerModal = ui.modal(modalPicker, () => {}, {
              buttons: ["Close"]
            });
            """
        );
        Assert.True(opened.Ok, opened.Error);
        var open = Assert.IsType<ModalEventMessage.FullStateMessage>(
            await ReadModalEventAsync(channel.Reader)
        );
        var picker = Assert.IsType<Element>(
            open.Projection.State.Root.Children[0].AsElement().Children[0].AsElement().Children[0]
        );
        var pickerId = Guid.Parse(picker.Attributes[FieldMarker.AttributeName]!);

        var begin = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("modal.txt", "text/plain", 1)]
        );

        Assert.True(begin.Ok, begin.Error);
        var mutation = await ReadModalEventAsync(channel.Reader);
        switch (mutation)
        {
            case ModalEventMessage.PatchMessage patch:
                Assert.Equal(1, patch.Revision);
                Assert.Contains(
                    patch.Operations,
                    operation =>
                        operation
                            is SetAttributeOperation
                            {
                                Name: "data-duetspad-attachment-revision",
                                Value: var value,
                            }
                        && value == begin.Revision.ToString()
                );
                break;
            case ModalEventMessage.FullStateMessage replace
                when replace.Type == ModalEventTypes.Replace:
                Assert.Equal(1, replace.Projection.Revision);
                var projectedPicker = Assert.IsType<Element>(
                    replace
                        .Projection.State.Root.Children[0]
                        .AsElement()
                        .Children[0]
                        .AsElement()
                        .Children[0]
                );
                Assert.Equal(
                    begin.Revision.ToString(),
                    projectedPicker.Attributes["data-duetspad-attachment-revision"]
                );
                break;
            default:
                throw new InvalidOperationException("Expected a modal attachment projection.");
        }

        var closed = await session.EvaluateAsync("pickerModal.close()");
        Assert.True(closed.Ok, closed.Error);
        Assert.IsType<ModalEventMessage.CloseMessage>(await ReadModalEventAsync(channel.Reader));

        var stale = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("stale.txt", "text/plain", 1)]
        );
        Assert.False(stale.Ok);
        Assert.Contains("no longer available", stale.Error);
    }

    [Fact]
    public async Task Modal_render_failure_returns_a_closed_handle_and_records_the_error()
    {
        var sentinel = new object();
        var duetsSession = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
        using var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            [new ThrowingRenderer(sentinel)]
        );
        session.DuetsSession.SetValue("__modalRenderFailure", sentinel);

        var result = await session.EvaluateAsync(
            """
            var failedModal = ui.modal(__modalRenderFailure, () => {});
            failedModal.isOpen;
            """
        );

        Assert.True(result.Ok, result.Error);
        Assert.Equal("false", result.Result);
        var entry = Assert.Single(session.Timeline.State);
        Assert.Equal("render-error", entry.Reason);
    }

    [Fact]
    public async Task Programmatic_close_does_not_invoke_result_callback()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            var callbackCount = 0;
            var activeModal = ui.modal(
              "body",
              () => callbackCount++,
              { buttons: ["Close"] }
            );
            """
        );
        Assert.True(opened.Ok, opened.Error);
        _ = await ReadModalEventAsync(channel.Reader);

        var closed = await session.EvaluateAsync(
            "activeModal.close(); dump(`${activeModal.isOpen}:${callbackCount}`)"
        );
        Assert.True(closed.Ok, closed.Error);
        Assert.IsType<ModalEventMessage.CloseMessage>(await ReadModalEventAsync(channel.Reader));
        Assert.Equal("false:0", Assert.IsType<Text>(session.Timeline.State[^1].Body).Value);
    }

    [Fact]
    public async Task Throwing_result_callback_still_closes_and_retires_actions()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """ui.modal("body", () => { throw new Error("boom"); }, { buttons: ["Run"] })"""
        );
        Assert.True(opened.Ok, opened.Error);
        var open = Assert.IsType<ModalEventMessage.FullStateMessage>(
            await ReadModalEventAsync(channel.Reader)
        );
        var action = open.Interactions[0];

        var invoked = await session.InvokeInteractionAsync(action.HandlerId);

        Assert.False(invoked.Ok);
        Assert.IsType<ModalEventMessage.CloseMessage>(await ReadModalEventAsync(channel.Reader));
        Assert.True((await session.InvokeInteractionAsync(action.HandlerId)).Stale);
    }

    [Fact]
    public async Task Active_modal_limit_rejects_an_additional_modal()
    {
        using var session = await CreatePadSessionAsync(maximum: 1);
        var first = await session.EvaluateAsync(
            """ui.modal("one", () => {}, { buttons: ["Close"] })"""
        );
        var second = await session.EvaluateAsync(
            """ui.modal("two", () => {}, { buttons: ["Close"] })"""
        );

        Assert.True(first.Ok, first.Error);
        Assert.False(second.Ok);
        Assert.Contains("more than 1 active modals", second.Error);
    }

    [Fact]
    public async Task Active_modal_limit_is_checked_before_rendering_the_rejected_body()
    {
        var sentinel = new object();
        var duetsSession = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
        using var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            [new ThrowingRenderer(sentinel)],
            maxActiveModals: 1
        );
        session.DuetsSession.SetValue("__rejectedModalBody", sentinel);
        var first = await session.EvaluateAsync(
            """ui.modal("one", () => {}, { buttons: ["Close"] })"""
        );

        var rejected = await session.EvaluateAsync(
            """ui.modal(__rejectedModalBody, () => {}, { buttons: ["Close"] })"""
        );

        Assert.True(first.Ok, first.Error);
        Assert.False(rejected.Ok);
        Assert.Contains("more than 1 active modals", rejected.Error);
        Assert.Empty(session.Timeline.State);
    }
}

internal static class ModalTestRenderNodeExtensions
{
    public static Element AsElement(this ITerminalRenderNode node) => Assert.IsType<Element>(node);
}
