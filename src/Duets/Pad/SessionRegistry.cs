using System.Collections.Concurrent;
using Timer = System.Timers.Timer;

namespace Duets.Pad;

/// <summary>
/// Owns the session dictionary, idle-cleanup timer, and session lifecycle operations
/// (create, delete, lookup, and idle eviction) for DuetsPad.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-38 invariants upheld here:</b>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>No session-identifier reuse.</b> Session IDs are generated via
///       <see cref="Guid.NewGuid"/> and are never reused after a session is removed from the
///       dictionary. A POST with a stale GUID that is not found in the registry always produces a
///       fresh ID.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Uniform "unknown session" responses.</b> Any lookup that does not find a session
///       returns <see langword="null"/>; the route layer is responsible for writing the uniform
///       <c>{ ok: false, error: "Unknown session." }</c> response.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Active-subscriber protection.</b> <see cref="RemoveIdleSessions"/> never evicts a
///       session that has at least one live SSE subscriber, regardless of the last-activity
///       timestamp. The registry queries <see cref="DuetsPadSession.HasActiveSubscribers"/>
///       (a read-through property on the session) but does not reach into session-internal locks.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Correct disposal.</b> Sessions removed by DELETE or by the idle sweep are disposed
///       after they are removed from the dictionary. A dispose failure is swallowed so it cannot
///       abort a sweep iteration or kill the cleanup timer.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// The session dictionary is a <see cref="ConcurrentDictionary{TKey,TValue}"/>; all structural
/// operations (add, remove, iterate) use its built-in lock-free semantics. The registry does not
/// impose additional locking on the dictionary.
/// </para>
/// </remarks>
internal sealed class SessionRegistry : IDisposable
{
    private readonly DuetsPadServiceOptions _options;
    private readonly ConcurrentDictionary<Guid, DuetsPadSession> _sessions = new();
    private readonly Timer? _cleanupTimer;

    internal SessionRegistry(DuetsPadServiceOptions options)
    {
        this._options = options ?? throw new ArgumentNullException(nameof(options));

        // Start the idle-cleanup sweep timer only when IdleTimeout is enabled.
        if (options.IdleTimeout is { } timeout && timeout > TimeSpan.Zero)
        {
            this._cleanupTimer = new Timer(options.CleanupInterval.TotalMilliseconds);
            this._cleanupTimer.Elapsed += (_, _) => this.RemoveIdleSessions();
            this._cleanupTimer.Start();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this._cleanupTimer is not null)
        {
            this._cleanupTimer.Stop();
            this._cleanupTimer.Dispose();
        }

        foreach (var (_, session) in this._sessions)
        {
            session.Dispose();
        }

        this._sessions.Clear();
    }

    /// <summary>
    /// Returns the <see cref="DuetsPadSession"/> identified by <paramref name="id"/>, or
    /// <see langword="null"/> if no such session exists. Exposed for testing only.
    /// </summary>
    internal DuetsPadSession? TryGetSession(Guid id) =>
        this._sessions.TryGetValue(id, out var session) ? session : null;

    /// <summary>
    /// Returns the <see cref="DuetsPadSession"/> identified by <paramref name="id"/>, or
    /// <see langword="null"/> if the ID is not a valid <see cref="Guid"/> or no matching session
    /// exists.
    /// </summary>
    internal DuetsPadSession? TryGetSession(string? id) =>
        Guid.TryParse(id, out var guid) ? this.TryGetSession(guid) : null;

    /// <summary>
    /// Returns the existing <see cref="DuetsPadSession"/> for <paramref name="existingId"/> when
    /// it is present, or creates a new session using <see cref="DuetsPadServiceOptions.SessionFactory"/>.
    /// </summary>
    /// <param name="existingId">
    /// An optional client-supplied session ID to reconnect to. When <see langword="null"/> or not
    /// found in the registry, a new session is always created with a fresh ID.
    /// </param>
    /// <returns>
    /// A tuple of the session and its ID. The session is the existing one when
    /// <paramref name="existingId"/> was found, or a newly created session otherwise.
    /// </returns>
    internal async Task<(DuetsPadSession Session, Guid Id)> GetOrCreateSessionAsync(
        Guid? existingId
    )
    {
        if (existingId.HasValue && this._sessions.TryGetValue(existingId.Value, out var existing))
        {
            return (existing, existingId.Value);
        }

        var duetsSession = await this._options.SessionFactory();
        var newId = Guid.NewGuid();
        var session = new DuetsPadSession(
            newId,
            duetsSession,
            this._options.ObjectRenderers,
            this._options.Clock,
            this._options.TimelineEntryLimit,
            this._options.DumpOptions
        );
        this._sessions[newId] = session;
        return (session, newId);
    }

    /// <summary>
    /// Removes and disposes the session identified by <paramref name="id"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> and the removed session's ID when the session was found and removed;
    /// <see langword="false"/> when no session with that ID exists.
    /// </returns>
    internal bool TryDeleteSession(Guid id)
    {
        if (!this._sessions.TryRemove(id, out var session))
        {
            return false;
        }

        // The session is already removed from the dictionary; a dispose failure must not
        // escape into the HTTP handler. There is no logger here, so observe and continue.
        try
        {
            session.Dispose();
        }
        catch
        {
            // Swallow: the session is orphaned but unreachable; nothing more to do.
        }

        return true;
    }

    /// <summary>
    /// Removes and disposes sessions that have been idle longer than
    /// <see cref="DuetsPadServiceOptions.IdleTimeout"/>. Does nothing when
    /// <see cref="DuetsPadServiceOptions.IdleTimeout"/> is <see langword="null"/> or non-positive.
    /// Called by the background cleanup timer; also directly callable by tests.
    /// </summary>
    internal void RemoveIdleSessions()
    {
        if (this._options.IdleTimeout is not { } timeout || timeout <= TimeSpan.Zero)
        {
            return;
        }

        var now = this._options.Clock();
        foreach (var (id, session) in this._sessions)
        {
            // Never evict a session that has a live SSE stream; the subscriber guard is
            // timing-independent and takes precedence over the LastActivity check.
            if (session.HasActiveSubscribers)
            {
                continue;
            }

            if (now - session.LastActivityUtc > timeout)
            {
                if (this._sessions.TryRemove(id, out var removed))
                {
                    // One session's dispose failure must not abort the sweep over the others,
                    // nor kill the cleanup timer. There is no logger here, so observe and continue.
                    try
                    {
                        removed.Dispose();
                    }
                    catch
                    {
                        // Swallow and proceed to the next idle session.
                    }
                }
            }
        }
    }
}
