using Duets.Pad.Interactions;
using Duets.Pad.Modals;

namespace Duets.Pad.Protocol;

internal abstract record ModalEventMessage
{
    private protected ModalEventMessage() { }

    internal sealed record SnapshotMessage(IReadOnlyList<ModalSnapshotItem> Modals)
        : ModalEventMessage;

    internal sealed record FullStateMessage(
        string Type,
        ModalProjection Projection,
        IReadOnlyList<CommittedInteraction> Interactions
    ) : ModalEventMessage;

    internal sealed record PatchMessage(
        Guid ModalId,
        long BaseRevision,
        long Revision,
        IReadOnlyList<CanvasPatchOperation> Operations,
        IReadOnlyList<CommittedInteraction> Interactions
    ) : ModalEventMessage;

    internal sealed record CloseMessage(Guid ModalId) : ModalEventMessage;

    public static ModalEventMessage Snapshot(IReadOnlyList<ModalSnapshotItem> modals) =>
        new SnapshotMessage(modals);

    public static ModalEventMessage Open(
        ModalProjection projection,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new FullStateMessage(ModalEventTypes.Open, projection, interactions);

    public static ModalEventMessage Replace(
        ModalProjection projection,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new FullStateMessage(ModalEventTypes.Replace, projection, interactions);

    public static ModalEventMessage Patch(
        Guid modalId,
        long baseRevision,
        long revision,
        IReadOnlyList<CanvasPatchOperation> operations,
        IReadOnlyList<CommittedInteraction> interactions
    ) => new PatchMessage(modalId, baseRevision, revision, operations, interactions);

    public static ModalEventMessage Close(Guid modalId) => new CloseMessage(modalId);
}
