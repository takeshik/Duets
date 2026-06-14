using System.Threading.Channels;
using Duets.Pad.Protocol;
using Duets.Pad.State;

namespace Duets.Pad;

/// <summary>
/// Grouped surface for the Canvas sub-API of a <see cref="DuetsPadSession"/>.
/// Holds only a back-reference to the session; all operations delegate to session methods
/// that run under the session's <c>_stateLock</c>. This type owns no state and no locks.
/// </summary>
internal sealed class CanvasFacade(DuetsPadSession session)
{
    private readonly DuetsPadSession _session =
        session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>
    /// The current immutable snapshot of the canvas. Reads the backing field on the session;
    /// the value is updated by canvas mutation operations under <c>_stateLock</c>.
    /// </summary>
    public CanvasState State => this._session.CanvasState;

    /// <summary>
    /// Renders <paramref name="value"/> and appends it to the canvas.
    /// Delegates to <see cref="DuetsPadSession.CanvasAdd"/>. Never throws.
    /// </summary>
    public void Add(object? value) => this._session.CanvasAdd(value);

    /// <summary>
    /// Renders <paramref name="value"/> and replaces the entire canvas with it.
    /// Delegates to <see cref="DuetsPadSession.CanvasSet"/>. Never throws.
    /// </summary>
    public void Set(object? value) => this._session.CanvasSet(value);

    /// <summary>
    /// Clears the canvas and enqueues a snapshot event.
    /// Delegates to <see cref="DuetsPadSession.CanvasClear"/>. Never throws.
    /// </summary>
    public void Clear() => this._session.CanvasClear();

    /// <summary>
    /// Registers a canvas SSE subscriber. Delegates to
    /// <see cref="DuetsPadSession.AddCanvasSubscriber"/>; the initial snapshot is enqueued
    /// under the session's <c>_stateLock</c> before this method returns.
    /// </summary>
    /// <returns>The registration key used to unregister via <see cref="Unsubscribe"/>.</returns>
    public Guid Subscribe(ChannelWriter<CanvasEventMessage?> writer) =>
        this._session.AddCanvasSubscriber(writer);

    /// <summary>
    /// Removes the canvas subscriber identified by <paramref name="key"/>.
    /// Delegates to <see cref="DuetsPadSession.RemoveCanvasSubscriber"/>.
    /// </summary>
    public void Unsubscribe(Guid key) => this._session.RemoveCanvasSubscriber(key);
}
