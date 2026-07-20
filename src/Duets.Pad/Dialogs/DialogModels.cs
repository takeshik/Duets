using Duets.Pad.Interactions;
using Duets.Pad.State;

namespace Duets.Pad.Dialogs;

internal sealed record DialogButtonDefinition(string Id, string Label, string Variant);

internal sealed record DialogOptions(
    string? Title,
    IReadOnlyList<DialogButtonDefinition> Buttons,
    string? DefaultButtonId,
    bool CanDismiss,
    string? DismissButtonId,
    string Size
);

internal sealed record DialogProjection(
    Guid Id,
    CanvasState State,
    long Revision,
    DialogOptions Options,
    bool Claimed = false
);

internal sealed record DialogSnapshotItem(
    DialogProjection Projection,
    IReadOnlyList<CommittedInteraction> Interactions
);
