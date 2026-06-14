using System.Threading.Channels;
using Duets.Pad.Protocol;
using Duets.Pad.State;

namespace Duets.Pad;

/// <summary>
/// Grouped surface for the Canvas sub-API of a <see cref="DuetsPadSession"/>.
/// All mutations run under the session's <c>_stateLock</c>; this interface owns no state and no locks.
/// </summary>
internal interface ICanvasSurface
{
    /// <summary>
    /// The current immutable snapshot of the canvas. Updated by canvas mutation operations under
    /// <c>_stateLock</c>.
    /// </summary>
    public CanvasState State { get; }

    /// <summary>
    /// Renders <paramref name="value"/> and appends it to the canvas. Never throws.
    /// </summary>
    public void Add(object? value);

    /// <summary>
    /// Renders <paramref name="value"/> and replaces the entire canvas with it. Never throws.
    /// </summary>
    public void Set(object? value);

    /// <summary>
    /// Clears the canvas and enqueues a snapshot event. Never throws.
    /// </summary>
    public void Clear();

    /// <summary>
    /// Registers a canvas SSE subscriber. A <c>canvas.snapshot</c> event for the current canvas
    /// state is enqueued to <paramref name="writer"/> before this method returns, under the same
    /// lock used for all subsequent updates (see ordering guarantee in <see cref="DuetsPadSession"/>
    /// remarks).
    /// </summary>
    /// <returns>The registration key used to unregister via <see cref="Unsubscribe"/>.</returns>
    public Guid Subscribe(ChannelWriter<CanvasEventMessage?> writer);

    /// <summary>
    /// Removes the canvas subscriber identified by <paramref name="key"/>.
    /// </summary>
    public void Unsubscribe(Guid key);
}
