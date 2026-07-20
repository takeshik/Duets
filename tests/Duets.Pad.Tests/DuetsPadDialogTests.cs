using System.Threading.Channels;
using Duets.Jint;
using Duets.Pad;
using Duets.Pad.Attachments;
using Duets.Pad.Protocol;
using Duets.Pad.Rendering;
using Duets.Tests.TestSupport;
using Jint;

namespace Duets.Pad.Tests;

public sealed class DuetsPadDialogTests
{
    private sealed class ThrowingRenderer(object sentinel) : IObjectRenderer
    {
        private readonly object _sentinel = sentinel;

        public bool CanRender(object? value) => ReferenceEquals(value, this._sentinel);

        public DisplayContent Render(object value, RenderContext context) =>
            throw new InvalidOperationException("deliberate dialog render failure");
    }

    private static async Task<DuetsPadSession> CreatePadSessionAsync(int? maximum = 8)
    {
        var duetsSession = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
        return new DuetsPadSession(Guid.NewGuid(), duetsSession, maxActiveDialogs: maximum);
    }

    private static async Task<DialogEventMessage> ReadDialogEventAsync(
        ChannelReader<PadEventMessage?> reader
    )
    {
        while (true)
        {
            var message = await reader.ReadAsync(TestContext.Current.CancellationToken);
            if (message is PadEventMessage.Dialog dialog)
            {
                return dialog.Message;
            }
        }
    }

    [Fact]
    public async Task Dialog_action_observes_latest_input_and_closes_once()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var evaluation = await session.EvaluateAsync(
            """
            var name = ui.textBox({ value: "initial" });
            ui.dialog(
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
        var open = Assert.IsType<DialogEventMessage.FullStateMessage>(
            await ReadDialogEventAsync(channel.Reader)
        );
        Assert.Equal(DialogEventTypes.Open, open.Type);
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
        Assert.IsType<DialogEventMessage.CloseMessage>(await ReadDialogEventAsync(channel.Reader));

        var duplicate = await session.InvokeInteractionAsync(save.HandlerId);
        Assert.True(duplicate.Stale);
    }

    [Fact]
    public async Task Active_dialog_is_replayed_to_a_new_subscriber()
    {
        using var session = await CreatePadSessionAsync();
        var evaluation = await session.EvaluateAsync(
            """ui.dialog("body", () => {}, { buttons: ["Close"] })"""
        );
        Assert.True(evaluation.Ok, evaluation.Error);

        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        var snapshot = Assert.IsType<DialogEventMessage.SnapshotMessage>(
            await ReadDialogEventAsync(channel.Reader)
        );

        Assert.Single(snapshot.Dialogs);
        Assert.Equal("Close", snapshot.Dialogs[0].Projection.Options.Buttons[0].Id);
    }

    [Fact]
    public async Task Dialog_slot_update_projects_a_new_revision()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            var dialogSlot = ui.slot("before");
            ui.dialog(dialogSlot, () => {}, { buttons: ["Close"] });
            """
        );
        Assert.True(opened.Ok, opened.Error);
        _ = await ReadDialogEventAsync(channel.Reader);

        var updated = await session.EvaluateAsync("dialogSlot.content = 'after'");
        Assert.True(updated.Ok, updated.Error);
        var message = await ReadDialogEventAsync(channel.Reader);

        var revision = message switch
        {
            DialogEventMessage.PatchMessage patch => patch.Revision,
            DialogEventMessage.FullStateMessage replace
                when replace.Type == DialogEventTypes.Replace => replace.Projection.Revision,
            _ => throw new InvalidOperationException("Expected a dialog mutation event."),
        };
        Assert.Equal(1, revision);
    }

    [Fact]
    public async Task Dialog_body_interaction_updates_content_without_closing()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            var bodySlot = ui.slot("before");
            var interactiveDialog = ui.dialog(
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
        var open = Assert.IsType<DialogEventMessage.FullStateMessage>(
            await ReadDialogEventAsync(channel.Reader)
        );
        var update = open.Interactions.Single(interaction =>
            interaction.Target.Segments.SequenceEqual([0, 0, 0, 1])
        );

        var invoked = await session.InvokeInteractionAsync(update.HandlerId);

        Assert.True(invoked.Ok, invoked.Error);
        var mutation = await ReadDialogEventAsync(channel.Reader);
        Assert.True(
            mutation
                is DialogEventMessage.PatchMessage
                    or DialogEventMessage.FullStateMessage { Type: DialogEventTypes.Replace }
        );
        var isOpen = await session.EvaluateAsync("interactiveDialog.isOpen");
        Assert.True(isOpen.Ok, isOpen.Error);
        Assert.Equal("true", isOpen.Result);
    }

    [Fact]
    public async Task Dialog_dismiss_reports_the_mapped_action_id()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            ui.dialog(
              "body",
              result => dump(`${result.reason}:${result.actionId}`),
              { buttons: ["Cancel"], dismissButtonId: "Cancel" }
            );
            """
        );
        Assert.True(opened.Ok, opened.Error);
        var open = Assert.IsType<DialogEventMessage.FullStateMessage>(
            await ReadDialogEventAsync(channel.Reader)
        );
        var dismiss = open.Interactions.Single(interaction =>
            interaction.Target.Segments.SequenceEqual([0, 2])
        );

        var invoked = await session.InvokeInteractionAsync(dismiss.HandlerId);

        Assert.True(invoked.Ok, invoked.Error);
        Assert.Equal("dismiss:Cancel", Assert.IsType<Text>(session.Timeline.State[^1].Body).Value);
        Assert.IsType<DialogEventMessage.CloseMessage>(await ReadDialogEventAsync(channel.Reader));
    }

    [Fact]
    public async Task Dialog_unmapped_dismiss_reports_null_action_id()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            ui.dialog(
              "body",
              result => dump(`${result.reason}:${result.actionId === null}`),
              { buttons: ["Close"] }
            );
            """
        );
        Assert.True(opened.Ok, opened.Error);
        var open = Assert.IsType<DialogEventMessage.FullStateMessage>(
            await ReadDialogEventAsync(channel.Reader)
        );
        var dismiss = open.Interactions.Single(interaction =>
            interaction.Target.Segments.SequenceEqual([0, 2])
        );

        var invoked = await session.InvokeInteractionAsync(dismiss.HandlerId);

        Assert.True(invoked.Ok, invoked.Error);
        Assert.Equal("dismiss:true", Assert.IsType<Text>(session.Timeline.State[^1].Body).Value);
        Assert.IsType<DialogEventMessage.CloseMessage>(await ReadDialogEventAsync(channel.Reader));
    }

    [Fact]
    public async Task Multiple_dialogs_are_replayed_in_open_order()
    {
        using var session = await CreatePadSessionAsync();
        var opened = await session.EvaluateAsync(
            """
            var firstDialog = ui.dialog("first", () => {}, {
              title: "First",
              buttons: ["Close"]
            });
            var secondDialog = ui.dialog("second", () => {}, {
              title: "Second",
              buttons: ["Close"]
            });
            """
        );
        Assert.True(opened.Ok, opened.Error);

        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        var snapshot = Assert.IsType<DialogEventMessage.SnapshotMessage>(
            await ReadDialogEventAsync(channel.Reader)
        );

        Assert.Equal(
            ["First", "Second"],
            snapshot.Dialogs.Select(dialog => dialog.Projection.Options.Title)
        );

        var closed = await session.EvaluateAsync("firstDialog.close()");
        Assert.True(closed.Ok, closed.Error);
        var close = Assert.IsType<DialogEventMessage.CloseMessage>(
            await ReadDialogEventAsync(channel.Reader)
        );
        Assert.Equal(snapshot.Dialogs[0].Projection.Id, close.DialogId);

        var reconnect = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(reconnect.Writer, session.DuetsSession.Declarations);
        var remaining = Assert.IsType<DialogEventMessage.SnapshotMessage>(
            await ReadDialogEventAsync(reconnect.Reader)
        );
        Assert.Equal("Second", Assert.Single(remaining.Dialogs).Projection.Options.Title);
    }

    [Fact]
    public async Task Dialog_file_picker_is_live_projected_and_pruned_on_close()
    {
        using var session = await CreatePadSessionAsync();
        var channel = Channel.CreateUnbounded<PadEventMessage?>();
        session.SubscribeEvents(channel.Writer, session.DuetsSession.Declarations);
        while (channel.Reader.TryRead(out _)) { }

        var opened = await session.EvaluateAsync(
            """
            var dialogPicker = ui.filePicker();
            var pickerDialog = ui.dialog(dialogPicker, () => {}, {
              buttons: ["Close"]
            });
            """
        );
        Assert.True(opened.Ok, opened.Error);
        var open = Assert.IsType<DialogEventMessage.FullStateMessage>(
            await ReadDialogEventAsync(channel.Reader)
        );
        var picker = Assert.IsType<Element>(
            open.Projection.State.Root.Children[0].AsElement().Children[0].AsElement().Children[0]
        );
        var pickerId = Guid.Parse(picker.Attributes[FieldMarker.AttributeName]!);

        var begin = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("dialog.txt", "text/plain", 1)]
        );

        Assert.True(begin.Ok, begin.Error);
        var mutation = await ReadDialogEventAsync(channel.Reader);
        switch (mutation)
        {
            case DialogEventMessage.PatchMessage patch:
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
            case DialogEventMessage.FullStateMessage replace
                when replace.Type == DialogEventTypes.Replace:
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
                throw new InvalidOperationException("Expected a dialog attachment projection.");
        }

        var closed = await session.EvaluateAsync("pickerDialog.close()");
        Assert.True(closed.Ok, closed.Error);
        Assert.IsType<DialogEventMessage.CloseMessage>(await ReadDialogEventAsync(channel.Reader));

        var stale = await session.BeginAttachmentSelectionAsync(
            pickerId,
            [new AttachmentFileManifest("stale.txt", "text/plain", 1)]
        );
        Assert.False(stale.Ok);
        Assert.Contains("no longer available", stale.Error);
    }

    [Fact]
    public async Task Dialog_render_failure_returns_a_closed_handle_and_records_the_error()
    {
        var sentinel = new object();
        var duetsSession = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
        using var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            [new ThrowingRenderer(sentinel)]
        );
        session.DuetsSession.SetValue("__dialogRenderFailure", sentinel);

        var result = await session.EvaluateAsync(
            """
            var failedDialog = ui.dialog(__dialogRenderFailure, () => {});
            failedDialog.isOpen;
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
            var activeDialog = ui.dialog(
              "body",
              () => callbackCount++,
              { buttons: ["Close"] }
            );
            """
        );
        Assert.True(opened.Ok, opened.Error);
        _ = await ReadDialogEventAsync(channel.Reader);

        var closed = await session.EvaluateAsync(
            "activeDialog.close(); dump(`${activeDialog.isOpen}:${callbackCount}`)"
        );
        Assert.True(closed.Ok, closed.Error);
        Assert.IsType<DialogEventMessage.CloseMessage>(await ReadDialogEventAsync(channel.Reader));
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
            """ui.dialog("body", () => { throw new Error("boom"); }, { buttons: ["Run"] })"""
        );
        Assert.True(opened.Ok, opened.Error);
        var open = Assert.IsType<DialogEventMessage.FullStateMessage>(
            await ReadDialogEventAsync(channel.Reader)
        );
        var action = open.Interactions[0];

        var invoked = await session.InvokeInteractionAsync(action.HandlerId);

        Assert.False(invoked.Ok);
        Assert.IsType<DialogEventMessage.CloseMessage>(await ReadDialogEventAsync(channel.Reader));
        Assert.True((await session.InvokeInteractionAsync(action.HandlerId)).Stale);
    }

    [Fact]
    public async Task Active_dialog_limit_rejects_an_additional_dialog()
    {
        using var session = await CreatePadSessionAsync(maximum: 1);
        var first = await session.EvaluateAsync(
            """ui.dialog("one", () => {}, { buttons: ["Close"] })"""
        );
        var second = await session.EvaluateAsync(
            """ui.dialog("two", () => {}, { buttons: ["Close"] })"""
        );

        Assert.True(first.Ok, first.Error);
        Assert.False(second.Ok);
        Assert.Contains("more than 1 active dialogs", second.Error);
    }

    [Fact]
    public async Task Active_dialog_limit_is_checked_before_rendering_the_rejected_body()
    {
        var sentinel = new object();
        var duetsSession = await JintTestRuntime.CreateSessionAsync(o => o.AllowClr());
        using var session = new DuetsPadSession(
            Guid.NewGuid(),
            duetsSession,
            [new ThrowingRenderer(sentinel)],
            maxActiveDialogs: 1
        );
        session.DuetsSession.SetValue("__rejectedDialogBody", sentinel);
        var first = await session.EvaluateAsync(
            """ui.dialog("one", () => {}, { buttons: ["Close"] })"""
        );

        var rejected = await session.EvaluateAsync(
            """ui.dialog(__rejectedDialogBody, () => {}, { buttons: ["Close"] })"""
        );

        Assert.True(first.Ok, first.Error);
        Assert.False(rejected.Ok);
        Assert.Contains("more than 1 active dialogs", rejected.Error);
        Assert.Empty(session.Timeline.State);
    }
}

internal static class DialogTestRenderNodeExtensions
{
    public static Element AsElement(this ITerminalRenderNode node) => Assert.IsType<Element>(node);
}
