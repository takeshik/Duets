using System.Threading.Channels;

namespace Duets.Pad;

/// <summary>
/// Grouped surface for the type-declaration subscriber sub-API of a <see cref="DuetsPadSession"/>.
/// All operations run under the session's <c>_stateLock</c>; this interface owns no state and no locks.
/// </summary>
/// <remarks>
/// Type-declaration broadcast lives in the SSE route, not in the session; only registration
/// and unregistration are exposed here. The initial declaration replay is the caller's
/// responsibility (the route performs it).
/// </remarks>
internal interface ITypeDeclarationsSurface
{
    /// <summary>
    /// Registers a type-declaration SSE subscriber. The caller is responsible for enqueuing
    /// existing declarations before or after this call (the route already does this).
    /// </summary>
    /// <returns>The registration key used to unregister via <see cref="Unsubscribe"/>.</returns>
    public Guid Subscribe(ChannelWriter<TypeDeclaration?> writer);

    /// <summary>
    /// Removes the type-declaration subscriber identified by <paramref name="key"/>.
    /// </summary>
    public void Unsubscribe(Guid key);
}
