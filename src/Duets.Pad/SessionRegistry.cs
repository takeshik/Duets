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
///       after they are removed from the dictionary. A dispose failure is reported through
///       <see cref="DuetsPadServiceOptions.SessionDisposalErrorHandler"/> and contained so it cannot
///       abort a sweep iteration or kill the cleanup timer.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// The session dictionary is a <see cref="ConcurrentDictionary{TKey,TValue}"/>; all structural
/// operations (add, remove, iterate) use its built-in thread-safe semantics. A lifecycle lock makes
/// publishing a newly constructed session atomic with registry disposal, so an asynchronous factory
/// cannot publish a session after teardown has begun; the same lock makes request acquisition atomic
/// with idle eviction, so a sweep cannot act on a pre-request activity timestamp.
/// </para>
/// </remarks>
internal sealed class SessionRegistry : IDisposable
{
    private readonly DuetsPadServiceOptions _options;
    private readonly ConcurrentDictionary<Guid, DuetsPadSession> _sessions = new();
    private readonly object _lifecycleLock = new();
    private readonly Timer? _cleanupTimer;

    // Admission counter for the MaxSessions cap (ADR-49). Counted separately from
    // _sessions.Count because a slot is reserved *before* the asynchronous SessionFactory runs and
    // released only when the session is removed: checking _sessions.Count instead would let every
    // concurrently in-flight create observe a below-cap count and construct an engine anyway,
    // turning a cap of 16 into as many sessions as the server has concurrent request slots.
    // Mutated only through Interlocked, and only via TryReserveSessionSlot/ReleaseSessionSlot.
    private int _sessionCount;
    private int _disposed;

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
        List<(Guid Id, DuetsPadSession Session)> sessionsToDispose = [];
        lock (this._lifecycleLock)
        {
            if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            {
                return;
            }

            if (this._cleanupTimer is not null)
            {
                this._cleanupTimer.Stop();
                this._cleanupTimer.Dispose();
            }

            foreach (var (id, _) in this._sessions)
            {
                if (this._sessions.TryRemove(id, out var session))
                {
                    this.ReleaseSessionSlot();
                    sessionsToDispose.Add((id, session));
                }
            }
        }

        foreach (var (id, session) in sessionsToDispose)
        {
            this.DisposeSessionAndReport(id, session);
        }
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
    /// Resolves a session for an incoming API operation and records activity atomically with
    /// respect to idle eviction. A session returned here cannot be selected as idle based on its
    /// pre-request timestamp by a concurrent cleanup sweep.
    /// </summary>
    internal DuetsPadSession? TryAcquireSession(Guid id)
    {
        lock (this._lifecycleLock)
        {
            if (!this._sessions.TryGetValue(id, out var session))
            {
                return null;
            }

            session.Touch();
            return session;
        }
    }

    internal DuetsPadSession? TryAcquireSession(string? id) =>
        Guid.TryParse(id, out var guid) ? this.TryAcquireSession(guid) : null;

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
    /// Reconnecting to an existing <paramref name="existingId"/> is always allowed regardless of
    /// <see cref="DuetsPadServiceOptions.MaxSessions"/>; <see langword="null"/> is returned instead
    /// when creating a brand-new session would exceed the cap (ADR-49). The cap is enforced
    /// atomically: a slot is reserved before the session factory runs, so concurrent creates cannot
    /// collectively exceed it.
    /// </returns>
    internal async Task<(DuetsPadSession Session, Guid Id)?> GetOrCreateSessionAsync(
        Guid? existingId
    )
    {
        if (existingId.HasValue && this.TryAcquireSession(existingId.Value) is { } existing)
        {
            return (existing, existingId.Value);
        }

        // Reserve the slot before the factory runs, so that concurrent creates cannot all pass the
        // cap check and then build engines in parallel.
        if (!this.TryReserveSessionSlot())
        {
            return null;
        }

        DuetsPadSession session;
        DuetsSession? duetsSession = null;
        IAttachmentStorage? attachmentStorage = null;
        Guid newId;
        try
        {
            duetsSession = await this._options.SessionFactory();
            newId = Guid.NewGuid();
            attachmentStorage =
                this._options.AttachmentStorageFactory(new AttachmentStorageContext(newId))
                ?? throw new InvalidOperationException(
                    "The attachment storage factory returned null."
                );
            session = new DuetsPadSession(
                newId,
                duetsSession,
                this._options.ObjectRenderers,
                this._options.Clock,
                this._options.TimelineEntryLimit,
                this._options.DumpOptions,
                this._options.EnableTaggedTemplateCompletions,
                this._options.TaggedTemplateCompletionRateLimitPerSecond,
                this._options.TaggedTemplateCompletionTimeout,
                attachmentStorage,
                this._options.MaxAttachmentBytesPerFile,
                this._options.MaxAttachmentBytesPerSession,
                this._options.MaxAttachmentsPerSession,
                this._options.AttachmentStorageDrainTimeout
            );

            // DuetsPadSession now owns and disposes both resources. Clearing the local ownership
            // markers prevents failure cleanup from disposing successfully transferred resources
            // if later code is added to this try block.
            duetsSession = null;
            attachmentStorage = null;
        }
        catch
        {
            // The reservation must not outlive a failed create, or repeated factory failures would
            // permanently consume the cap.
            this.ReleaseSessionSlot();

            // SessionFactory may have completed before DuetsPadSession construction failed (for
            // example while SessionBootstrap wires globals into the backend). Until construction
            // succeeds, ownership remains here and the engine must be torn down deterministically.
            // Preserve the original construction exception if teardown also fails.
            try
            {
                duetsSession?.Dispose();
            }
            catch
            {
                // Swallow: the construction failure is the actionable error for the caller.
            }

            try
            {
                attachmentStorage?.Dispose();
            }
            catch
            {
                // Swallow: the construction failure is the actionable error for the caller.
            }

            throw;
        }

        lock (this._lifecycleLock)
        {
            if (Volatile.Read(ref this._disposed) == 0)
            {
                this._sessions[newId] = session;
                return (session, newId);
            }
        }

        // The factory ran concurrently with Dispose. The lifecycle lock prevents publication after
        // teardown, but construction happened outside that lock and still owns a reserved slot.
        this.ReleaseSessionSlot();
        this.DisposeSessionAndReport(newId, session);

        throw new ObjectDisposedException(nameof(SessionRegistry));
    }

    /// <summary>
    /// Atomically claims one slot against <see cref="DuetsPadServiceOptions.MaxSessions"/>.
    /// Returns <see langword="false"/> when the cap is already reached, in which case no slot is
    /// consumed.
    /// </summary>
    private bool TryReserveSessionSlot()
    {
        // Compare-exchange loop rather than Increment-then-Decrement-on-overflow: the latter would
        // let a burst of rejected requests transiently push the counter above the cap, which a
        // concurrent reserve could observe. The counter is maintained even when MaxSessions is
        // null so it always reflects the live + in-flight total if a retained options instance is
        // later changed from unlimited to a finite cap.
        while (true)
        {
            if (Volatile.Read(ref this._disposed) != 0)
            {
                return false;
            }

            var current = Volatile.Read(ref this._sessionCount);
            if (
                current == int.MaxValue
                || (this._options.MaxSessions is { } maxSessions && current >= maxSessions)
            )
            {
                return false;
            }

            if (
                Interlocked.CompareExchange(ref this._sessionCount, current + 1, current) == current
            )
            {
                return true;
            }
        }
    }

    private void ReleaseSessionSlot() => Interlocked.Decrement(ref this._sessionCount);

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

        this.ReleaseSessionSlot();

        // The session is already removed from the dictionary; report a dispose failure without
        // letting it escape into the HTTP handler.
        this.DisposeSessionAndReport(id, session);

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

        List<(Guid Id, DuetsPadSession Session)> sessionsToDispose = [];
        lock (this._lifecycleLock)
        {
            if (Volatile.Read(ref this._disposed) != 0)
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

                if (
                    now - session.LastActivityUtc > timeout
                    && this._sessions.TryRemove(id, out var removed)
                )
                {
                    this.ReleaseSessionSlot();
                    sessionsToDispose.Add((id, removed));
                }
            }
        }

        foreach (var (id, session) in sessionsToDispose)
        {
            this.DisposeSessionAndReport(id, session);
        }
    }

    private void DisposeSessionAndReport(Guid id, DuetsPadSession session)
    {
        try
        {
            session.Dispose();
        }
        catch (Exception ex)
        {
            // A teardown failure must not abort a registry sweep or escape into an HTTP handler.
            // Give the host an observation path, while isolating failures in its diagnostic code.
            try
            {
                this._options.SessionDisposalErrorHandler?.Invoke(id, ex);
            }
            catch
            {
                // Diagnostics are best-effort and cannot become a second teardown failure.
            }
        }
    }
}
