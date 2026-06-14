using System.Threading.Channels;
using Duets.Pad.Protocol;
using Duets.Pad.Timeline;

namespace Duets.Pad;

/// <summary>
/// Grouped surface for the Timeline sub-API of a <see cref="DuetsPadSession"/>.
/// Holds only a back-reference to the session; all operations delegate to session methods
/// that run under the session's <c>_stateLock</c>. This type owns no state and no locks.
/// </summary>
internal sealed class TimelineFacade(DuetsPadSession session)
{
    private readonly DuetsPadSession _session =
        session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>
    /// The current immutable snapshot of the timeline. Reads the backing field on the session;
    /// the value is updated by timeline mutation operations under <c>_stateLock</c>.
    /// </summary>
    public TimelineState State => this._session.TimelineState;

    /// <summary>
    /// Registers a timeline SSE subscriber. Delegates to
    /// <see cref="DuetsPadSession.AddTimelineSubscriber"/>; the initial reset event is enqueued
    /// under the session's <c>_stateLock</c> before this method returns.
    /// </summary>
    /// <returns>The registration key used to unregister via <see cref="Unsubscribe"/>.</returns>
    public Guid Subscribe(ChannelWriter<TimelineEventMessage?> writer) =>
        this._session.AddTimelineSubscriber(writer);

    /// <summary>
    /// Removes the timeline subscriber identified by <paramref name="key"/>.
    /// Delegates to <see cref="DuetsPadSession.RemoveTimelineSubscriber"/>.
    /// </summary>
    public void Unsubscribe(Guid key) => this._session.RemoveTimelineSubscriber(key);
}
