using System.Threading.Channels;

namespace Duets.Pad;

/// <summary>
/// Grouped surface for the type-declaration subscriber sub-API of a <see cref="DuetsPadSession"/>.
/// Holds only a back-reference to the session; all operations delegate to session methods
/// that run under the session's <c>_stateLock</c>. This type owns no state and no locks.
/// </summary>
/// <remarks>
/// Type-declaration broadcast lives in the SSE route, not in the session; only registration
/// and unregistration are exposed here. The initial declaration replay is the caller's
/// responsibility (the route performs it).
/// </remarks>
internal sealed class TypeDeclarationsFacade(DuetsPadSession session)
{
    private readonly DuetsPadSession _session =
        session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>
    /// Registers a type-declaration SSE subscriber. Delegates to
    /// <see cref="DuetsPadSession.AddTypeDeclarationSubscriber"/>. The caller is responsible
    /// for enqueuing existing declarations; the route already does this.
    /// </summary>
    /// <returns>The registration key used to unregister via <see cref="Unsubscribe"/>.</returns>
    public Guid Subscribe(ChannelWriter<TypeDeclaration?> writer) =>
        this._session.AddTypeDeclarationSubscriber(writer);

    /// <summary>
    /// Removes the type-declaration subscriber identified by <paramref name="key"/>.
    /// Delegates to <see cref="DuetsPadSession.RemoveTypeDeclarationSubscriber"/>.
    /// </summary>
    public void Unsubscribe(Guid key) => this._session.RemoveTypeDeclarationSubscriber(key);
}
