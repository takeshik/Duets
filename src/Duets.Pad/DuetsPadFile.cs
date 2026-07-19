using System.Text;
using Duets.Pad.Attachments;

namespace Duets.Pad;

/// <summary>
/// Immutable metadata and read capability for one file committed through a DuetsPad file picker.
/// </summary>
public sealed class DuetsPadFile
{
    private readonly IFilePickerHost _host;
    private readonly Guid _pickerId;
    private readonly Guid _fileId;

    internal DuetsPadFile(
        IFilePickerHost host,
        Guid pickerId,
        Guid fileId,
        string name,
        string contentType,
        long size
    )
    {
        this._host = host ?? throw new ArgumentNullException(nameof(host));
        this._pickerId = pickerId;
        this._fileId = fileId;
        this.Id = fileId.ToString("D");
        this.Name = name ?? throw new ArgumentNullException(nameof(name));
        this.ContentType = contentType ?? "";
        this.Size =
            size >= 0
                ? size
                : throw new ArgumentOutOfRangeException(
                    nameof(size),
                    "File size cannot be negative."
                );
    }

    /// <summary>Opaque server-issued file identifier, scoped to the owning picker.</summary>
    public string Id { get; }

    /// <summary>Sanitized client-supplied leaf filename.</summary>
    public string Name { get; }

    /// <summary>Untrusted client-supplied media type, or an empty string when unavailable.</summary>
    public string ContentType { get; }

    /// <summary>Committed file length in bytes.</summary>
    public long Size { get; }

    /// <summary>
    /// Opens a fresh read-only stream positioned at zero. The caller is responsible for disposing
    /// the stream. Throws when the file is no longer retained by rendered DuetsPad output.
    /// </summary>
    public Stream OpenRead() => this._host.OpenRead(this._pickerId, this._fileId);

    /// <summary>
    /// Reads the complete file into a single-use byte buffer and releases the read lease. A script
    /// backend may consume the buffer as its native mutable byte-array type without another copy.
    /// </summary>
    public ScriptByteBuffer ReadAllBytes()
    {
        if (this.Size > int.MaxValue)
        {
            throw new InvalidOperationException(
                "The attachment is too large to materialize as one byte array."
            );
        }

        var bytes = new byte[(int)this.Size];
        using var stream = this.OpenRead();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "The attachment ended before its committed size was read."
                );
            }

            offset += read;
        }

        return ScriptByteBuffer.TakeOwnership(
            bytes,
            $"{nameof(DuetsPadFile)}.{nameof(this.ReadAllBytes)}"
        );
    }

    /// <summary>
    /// Reads the complete file as text and releases the read lease. UTF-8 is used unless a byte
    /// order mark identifies another Unicode encoding.
    /// </summary>
    public string ReadAllText()
    {
        using var stream = this.OpenRead();
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );
        return reader.ReadToEnd();
    }
}
