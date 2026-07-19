namespace Duets.Pad.Attachments;

/// <summary>Session-side callback surface used by <see cref="DisplayFilePicker"/>.</summary>
internal interface IFilePickerHost
{
    public AttachmentPickerSnapshot EnsureFilePicker(DisplayFilePicker picker);

    public IReadOnlyList<DuetsPadFile> GetFiles(Guid pickerId);

    public Stream OpenRead(Guid pickerId, Guid fileId);

    public void RemoveFile(DisplayFilePicker picker, Guid fileId);

    public void ClearFiles(DisplayFilePicker picker);
}
