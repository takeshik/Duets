using Duets.Pad.Attachments;
using Duets.Pad.Rendering;

namespace Duets.Pad;

/// <summary>
/// File-picker display handle whose committed files live in the owning DuetsPad session.
/// </summary>
public sealed class DisplayFilePicker
{
    private readonly FilePickerOptions _options;

    internal DisplayFilePicker(IFilePickerHost host, Guid id, FilePickerOptions options)
    {
        this.Host = host ?? throw new ArgumentNullException(nameof(host));
        this.Id =
            id != Guid.Empty
                ? id
                : throw new ArgumentException("Picker id cannot be empty.", nameof(id));
        this._options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The stable identity used to locate this picker in projected output.</summary>
    public Guid Id { get; }

    /// <summary>
    /// The currently committed files. Each access returns an independent read-only snapshot.
    /// </summary>
    public IReadOnlyList<DuetsPadFile> Files => [.. this.Host.GetFiles(this.Id)];

    /// <summary>Removes a committed file by its opaque <see cref="DuetsPadFile.Id"/>.</summary>
    public void Remove(string fileId)
    {
        if (!Guid.TryParse(fileId, out var parsed) || parsed == Guid.Empty)
        {
            throw new ArgumentException("File id must be a non-empty GUID.", nameof(fileId));
        }

        this.Host.RemoveFile(this, parsed);
    }

    /// <summary>Removes every committed file and cancels an unsettled selection.</summary>
    public void Clear() => this.Host.ClearFiles(this);

    internal bool Multiple => this._options.Multiple;

    internal IFilePickerHost Host { get; }

    // Browser ordering survives attachment-store pruning when a script later renders the same
    // handle again. Access is serialized by AttachmentStore's lock.
    internal Guid LastClientId { get; set; }

    internal long LastClientSequence { get; set; }

    internal DisplayContent Render()
    {
        // Rendering establishes attachment-store presence. It is intentionally not pure:
        // speculative or abandoned output is removed by reachability pruning before invocation.
        var snapshot = this.Host.EnsureFilePicker(this);
        return DisplayContent.FilePicker(this.Id, snapshot, this._options);
    }
}
