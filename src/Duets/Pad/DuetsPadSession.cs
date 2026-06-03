using Duets.Pad.Rendering;
using Duets.Pad.State;
using Duets.Pad.Timeline;

namespace Duets.Pad;

/// <summary>
/// Isolated server-side state for one DuetsPad browser session.
/// </summary>
/// <remarks>
/// A DuetsPad session owns one DuetsSession and keeps the associated Canvas,
/// Timeline, and object-renderer state together. The service layer is
/// responsible for creating session identifiers, routing HTTP/SSE requests to
/// the matching session, and disposing idle or explicitly reset sessions.
/// </remarks>
internal sealed class DuetsPadSession(Guid id, DuetsSession duetsSession) : IDisposable
{
    public Guid Id { get; } =
        id == Guid.Empty
            ? throw new ArgumentException("Session id cannot be empty.", nameof(id))
            : id;

    public DuetsSession DuetsSession { get; } =
        duetsSession ?? throw new ArgumentNullException(nameof(duetsSession));

    public CanvasState Canvas { get; private set; } = CanvasState.Empty;

    public TimelineState Timeline { get; private set; } = TimelineState.Empty;

    public IReadOnlyList<IObjectRenderer> ObjectRenderers { get; private set; } = [];

    public void SetCanvas(CanvasState canvas)
    {
        this.Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
    }

    public void SetTimeline(TimelineState timeline)
    {
        this.Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
    }

    public void SetObjectRenderers(IReadOnlyList<IObjectRenderer> objectRenderers)
    {
        this.ObjectRenderers =
            objectRenderers ?? throw new ArgumentNullException(nameof(objectRenderers));
    }

    public void Dispose() => this.DuetsSession.Dispose();
}
