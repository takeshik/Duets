using System.Threading.Channels;
using Duets.Pad.Protocol;
using Duets.Pad.Timeline;

namespace Duets.Pad;

/// <summary>
/// Grouped surface for the Timeline sub-API of a <see cref="DuetsPadSession"/>.
/// All mutations run under the session's <c>_stateLock</c>; this interface owns no state and no locks.
/// </summary>
internal interface ITimelineSurface
{
    /// <summary>
    /// The current immutable snapshot of the timeline. Updated by timeline mutation operations
    /// under <c>_stateLock</c>.
    /// </summary>
    public TimelineState State { get; }

    /// <summary>
    /// Registers a timeline SSE subscriber. A <c>timeline.reset</c> event for the current
    /// timeline state is enqueued to <paramref name="writer"/> before this method returns, under
    /// the same lock used for all subsequent updates (see ordering guarantee in
    /// <see cref="DuetsPadSession"/> remarks).
    /// </summary>
    /// <returns>The registration key used to unregister via <see cref="Unsubscribe"/>.</returns>
    public Guid Subscribe(ChannelWriter<TimelineEventMessage?> writer);

    /// <summary>
    /// Removes the timeline subscriber identified by <paramref name="key"/>.
    /// </summary>
    public void Unsubscribe(Guid key);
}
