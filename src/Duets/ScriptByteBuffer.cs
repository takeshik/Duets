using System.Runtime.CompilerServices;

namespace Duets;

/// <summary>
/// Transfers an exclusively owned byte array from host code to a script backend for projection as
/// the backend's native mutable byte-buffer type.
/// </summary>
/// <remarks>
/// <para>
/// This is an explicit ownership-transfer envelope, not general-purpose byte storage. After
/// passing an instance to a script engine, neither the instance nor the source array may be used
/// again. A backend may consume the instance only once.
/// </para>
/// <para>
/// Backends that do not provide a specialized projection expose this object according to their
/// normal host-object interoperability rules.
/// </para>
/// </remarks>
public sealed class ScriptByteBuffer
{
    private byte[]? _bytes;
    private readonly string _producer;

    private ScriptByteBuffer(byte[] bytes, string? producer)
    {
        this._bytes = bytes;
        this._producer = string.IsNullOrWhiteSpace(producer) ? "unknown host API" : producer;
        this.Length = bytes.Length;
    }

    /// <summary>Gets the number of bytes transferred by this instance.</summary>
    public int Length { get; }

    /// <summary>
    /// Creates a buffer by taking exclusive ownership of <paramref name="bytes"/> without copying.
    /// </summary>
    /// <param name="bytes">
    /// The array to transfer. The caller must not read or modify it after this method returns.
    /// </param>
    /// <param name="producer">
    /// Diagnostic name of the host API transferring the array. The calling member name is used by
    /// default and is included in a double-consumption error.
    /// </param>
    /// <returns>A single-use ownership-transfer envelope.</returns>
    public static ScriptByteBuffer TakeOwnership(
        byte[] bytes,
        [CallerMemberName] string? producer = null
    ) => new(bytes ?? throw new ArgumentNullException(nameof(bytes)), producer);

    /// <summary>
    /// Consumes the buffer and transfers its backing array to a script backend without copying.
    /// </summary>
    /// <returns>The exclusively owned backing array.</returns>
    /// <exception cref="InvalidOperationException">The buffer was already consumed.</exception>
    public byte[] Consume()
    {
        var bytes = Interlocked.Exchange(ref this._bytes, null);
        return bytes
            ?? throw new InvalidOperationException(
                $"The script byte buffer produced by '{this._producer}' was already consumed. Return a fresh buffer for each script result."
            );
    }
}
