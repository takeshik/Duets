namespace Duets.Pad.Attachments;

/// <summary>
/// Read-only stream facade that hides the underlying storage stream type and releases a blob lease
/// exactly once when closed.
/// </summary>
internal sealed class LeasedReadStream(Stream inner, Action release) : Stream
{
    private Stream? _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private Action? _release = release ?? throw new ArgumentNullException(nameof(release));

    private Stream Inner =>
        this._inner ?? throw new ObjectDisposedException(nameof(LeasedReadStream));

    public override bool CanRead => this._inner?.CanRead == true;
    public override bool CanSeek => this._inner?.CanSeek == true;
    public override bool CanWrite => false;
    public override long Length => this.Inner.Length;

    public override long Position
    {
        get => this.Inner.Position;
        set => this.Inner.Position = value;
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) =>
        this.Inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => this.Inner.Read(buffer);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => this.Inner.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => this.Inner.Seek(offset, origin);

    public override void SetLength(long value) =>
        throw new NotSupportedException("Attachment streams are read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Attachment streams are read-only.");

    protected override void Dispose(bool disposing)
    {
        var stream = Interlocked.Exchange(ref this._inner, null);
        var releaseLease = Interlocked.Exchange(ref this._release, null);
        try
        {
            if (disposing)
            {
                stream?.Dispose();
            }
        }
        finally
        {
            releaseLease?.Invoke();
            base.Dispose(disposing);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        var stream = Interlocked.Exchange(ref this._inner, null);
        var releaseLease = Interlocked.Exchange(ref this._release, null);
        try
        {
            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            releaseLease?.Invoke();
            GC.SuppressFinalize(this);
        }
    }

    internal void ForceClose()
    {
        try
        {
            this.Dispose();
        }
        catch
        {
            // Forced session teardown must continue closing the remaining leases and storage. The
            // lease callback has already run from Dispose's finally block.
        }
    }
}
