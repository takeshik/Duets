namespace Duets.Pad;

/// <summary>
/// Stores attachment blobs for one DuetsPad session. DuetsPad supplies opaque blob identifiers;
/// implementations must not derive storage paths from browser-supplied filenames.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe because operations for different blob identifiers may
/// overlap, must honor supplied cancellation tokens promptly, and must make deletion idempotent so
/// the same identifier can be retried. DuetsPad guarantees that operations for one blob are
/// serialized, never requests deletion while a read lease is active, and calls
/// <see cref="IDisposable.Dispose"/> only after tracked operations have drained. If draining exceeds
/// <see cref="DuetsPadServiceOptions.AttachmentStorageDrainTimeout"/>, that ordering is preserved by
/// background cleanup rather than force-disposing the storage.
/// </remarks>
public interface IAttachmentStorage : IDisposable
{
    /// <summary>Creates a new writable staging stream for <paramref name="blobId"/>.</summary>
    /// <param name="blobId">An opaque server-issued blob identifier.</param>
    /// <param name="cancellationToken">Cancels stream creation.</param>
    /// <returns>A writable stream positioned at zero.</returns>
    public ValueTask<Stream> CreateWriteStreamAsync(
        Guid blobId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Makes the completely written staging blob available for reading.</summary>
    /// <param name="blobId">The opaque blob identifier passed to <see cref="CreateWriteStreamAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the commit operation.</param>
    public ValueTask CommitAsync(Guid blobId, CancellationToken cancellationToken = default);

    /// <summary>Opens a fresh readable stream for a committed blob.</summary>
    /// <param name="blobId">The opaque server-issued blob identifier.</param>
    /// <returns>A readable stream positioned at zero.</returns>
    public Stream OpenRead(Guid blobId);

    /// <summary>
    /// Idempotently deletes staging and committed data for a blob if present. Implementations should
    /// honor cancellation promptly; DuetsPad may retry a failure with the same identifier.
    /// </summary>
    /// <param name="blobId">The opaque server-issued blob identifier.</param>
    /// <param name="cancellationToken">Cancels the deletion operation.</param>
    public ValueTask DeleteAsync(Guid blobId, CancellationToken cancellationToken = default);
}
