using Duets.Pad.Dialogs;
using Duets.Pad.Interactions;

namespace Duets.Pad.Protocol;

internal abstract record DialogEventMessage
{
    private protected DialogEventMessage() { }

    internal sealed record SnapshotMessage(IReadOnlyList<DialogSnapshotItem> Dialogs)
        : DialogEventMessage;

    internal sealed record FullStateMessage(
        string Type,
        DialogProjection Projection,
        IReadOnlyList<CommittedInteraction> Interactions
    ) : DialogEventMessage;

    internal sealed record PatchMessage(
        Guid DialogId,
        long BaseRevision,
        long Revision,
        IReadOnlyList<CanvasPatchOperation> Operations,
        IReadOnlyList<CommittedInteraction> Interactions
    ) : DialogEventMessage;

    internal sealed record CloseMessage(Guid DialogId) : DialogEventMessage;

    public static DialogEventMessage Snapshot(IReadOnlyList<DialogSnapshotItem> dialogs) =>
        new SnapshotMessage(dialogs);

    public static DialogEventMessage Open(
        DialogProjection projection,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new FullStateMessage(DialogEventTypes.Open, projection, interactions);

    public static DialogEventMessage Replace(
        DialogProjection projection,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new FullStateMessage(DialogEventTypes.Replace, projection, interactions);

    public static DialogEventMessage Patch(
        Guid dialogId,
        long baseRevision,
        long revision,
        IReadOnlyList<CanvasPatchOperation> operations,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new PatchMessage(dialogId, baseRevision, revision, operations, interactions);

    public static DialogEventMessage Close(Guid dialogId) => new CloseMessage(dialogId);
}
