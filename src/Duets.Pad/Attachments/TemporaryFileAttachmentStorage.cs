namespace Duets.Pad.Attachments;

/// <summary>Per-session temporary-file implementation of <see cref="IAttachmentStorage"/>.</summary>
internal sealed class TemporaryFileAttachmentStorage : IAttachmentStorage
{
    private readonly string _directory;
    private int _disposed;

    public TemporaryFileAttachmentStorage(AttachmentStorageContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.SessionId == Guid.Empty)
        {
            throw new ArgumentException("Session id cannot be empty.", nameof(context));
        }

        this._directory = Path.Combine(
            Path.GetTempPath(),
            "DuetsPad",
            $"{context.SessionId:N}-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(this._directory);
    }

    public ValueTask<Stream> CreateWriteStreamAsync(
        Guid blobId,
        CancellationToken cancellationToken = default
    )
    {
        this.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            this.StagingPath(blobId),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read | FileShare.Delete,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        return new ValueTask<Stream>(stream);
    }

    public ValueTask CommitAsync(Guid blobId, CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(this.StagingPath(blobId), this.CommittedPath(blobId));
        return default;
    }

    public Stream OpenRead(Guid blobId)
    {
        this.ThrowIfDisposed();
        return new FileStream(
            this.CommittedPath(blobId),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
    }

    public ValueTask DeleteAsync(Guid blobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteIfPresent(this.StagingPath(blobId));
        DeleteIfPresent(this.CommittedPath(blobId));
        return default;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
        {
            return;
        }

        if (Directory.Exists(this._directory))
        {
            Directory.Delete(this._directory, recursive: true);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string StagingPath(Guid blobId) =>
        Path.Combine(this._directory, ValidateBlobId(blobId) + ".upload");

    private string CommittedPath(Guid blobId) =>
        Path.Combine(this._directory, ValidateBlobId(blobId) + ".blob");

    private static string ValidateBlobId(Guid blobId) =>
        blobId != Guid.Empty
            ? blobId.ToString("N")
            : throw new ArgumentException("Blob id cannot be empty.", nameof(blobId));

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref this._disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(TemporaryFileAttachmentStorage));
        }
    }
}
