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
}
