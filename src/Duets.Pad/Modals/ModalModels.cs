using Duets.Pad.Interactions;
using Duets.Pad.State;

namespace Duets.Pad.Modals;

internal sealed record ModalButtonDefinition(string Id, string Label, string Variant);

internal sealed record ModalOptions(
    string? Title,
    IReadOnlyList<ModalButtonDefinition> Buttons,
    string? DefaultButtonId,
    bool CanDismiss,
    string? DismissButtonId,
    string Size
);

internal sealed record ModalProjection(
    Guid Id,
    CanvasState State,
    long Revision,
    ModalOptions Options,
    bool Claimed = false
);

internal sealed record ModalSnapshotItem(
    ModalProjection Projection,
    IReadOnlyList<CommittedInteraction> Interactions
);
