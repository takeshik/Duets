using Duets.Pad.State;

namespace Duets.Pad;

/// <summary>
/// Grouped surface for the Canvas sub-API of a <see cref="DuetsPadSession"/>.
/// All mutations run under the session's <c>_stateLock</c>; this interface owns no state and no locks.
/// </summary>
internal interface ICanvasSurface
{
    /// <summary>
    /// The name identifying this canvas within the session.
    /// </summary>
    public string Name { get; }

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
}
