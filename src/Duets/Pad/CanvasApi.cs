namespace Duets.Pad;

/// <summary>
/// Host object bound to the <c>canvas</c> global in script. Provides the three canvas mutation
/// methods of the DuetsPad protocol: <see cref="Add"/> (append), <see cref="Set"/> (replace),
/// and <see cref="Clear"/>.
/// </summary>
/// <remarks>
/// JS calls are camelCase; Jint maps them to the PascalCase CLR methods below.
/// All calls delegate to the owning <see cref="DuetsPadSession"/> ops which run on the eval
/// call stack (no extra locking required here).
/// </remarks>
internal sealed class CanvasApi(DuetsPadSession session)
{
    private readonly DuetsPadSession _session =
        session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Appends a rendered node to the canvas. (JS: <c>canvas.add</c>)</summary>
    public void Add(object? value) => this._session.Canvas.Add(value);

    /// <summary>Replaces the entire canvas with a single rendered node. (JS: <c>canvas.set</c>)</summary>
    public void Set(object? value) => this._session.Canvas.Set(value);

    /// <summary>Clears the canvas. (JS: <c>canvas.clear</c>)</summary>
    public void Clear() => this._session.Canvas.Clear();
}
