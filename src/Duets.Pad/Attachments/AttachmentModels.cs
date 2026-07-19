namespace Duets.Pad.Attachments;

internal enum AttachmentSelectionStatus
{
    Stable,
    Uploading,
    Failed,
}

internal sealed record AttachmentFileManifest(string Name, string ContentType, long Size);

internal sealed record AttachmentSelectionOrder(Guid ClientId, long Sequence);

internal sealed record AttachmentPickerSnapshot(
    long Revision,
    AttachmentSelectionStatus Status,
    IReadOnlyList<DuetsPadFile> Files,
    string? Error
);

internal sealed record AttachmentSelectionFile(Guid Id, string Name, string ContentType, long Size);

internal sealed record BeginAttachmentSelectionResult(
    bool Ok,
    Guid Token,
    long Revision,
    IReadOnlyList<AttachmentSelectionFile> Files,
    string? Error,
    bool TooLarge
);

internal sealed record AttachmentOperationResult(bool Ok, bool Stale, long Revision, string? Error);

internal sealed record AttachmentInvokeValidationResult(bool Ok, string? Error)
{
    public static AttachmentInvokeValidationResult Success { get; } = new(true, null);
}
