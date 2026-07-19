namespace Duets.Pad.Interactions;

internal sealed record InteractionInvokeResult(
    bool Ok,
    string? Error,
    bool Stale,
    bool AttachmentConflict = false
)
{
    public static InteractionInvokeResult Success { get; } =
        new(Ok: true, Error: null, Stale: false);

    public static InteractionInvokeResult Failed(string error) =>
        new(Ok: false, Error: error, Stale: false);

    public static InteractionInvokeResult StaleHandler(string error) =>
        new(Ok: false, Error: error, Stale: true);

    public static InteractionInvokeResult AttachmentStateChanged(string error) =>
        new(Ok: false, Error: error, Stale: false, AttachmentConflict: true);
}
