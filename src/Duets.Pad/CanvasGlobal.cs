namespace Duets.Pad;

/// <summary>
/// Host object bound to a named canvas in script. Provides the three canvas mutation
/// methods of the DuetsPad protocol: <see cref="Add"/> (append), <see cref="Set"/> (replace),
/// and <see cref="Clear"/>.
/// </summary>
/// <remarks>
/// JS calls are camelCase; Jint maps them to the PascalCase CLR methods below.
/// All calls delegate to the owning <see cref="DuetsPadSession"/> named-canvas methods which run
/// on the eval call stack (no extra locking required here).
/// </remarks>
internal sealed class CanvasGlobal(DuetsPadSession session, string name)
{
    private readonly DuetsPadSession _session =
        session ?? throw new ArgumentNullException(nameof(session));

    private readonly string _name = !string.IsNullOrEmpty(name)
        ? name
        : throw new ArgumentException("Canvas name must be non-empty.", nameof(name));

    /// <summary>Appends a rendered node to this canvas. (JS: <c>canvas.add</c>)</summary>
    public void Add(object? value) => this._session.CanvasAdd(this._name, value);

    /// <summary>Replaces the entire canvas with a single rendered node. (JS: <c>canvas.set</c>)</summary>
    public void Set(object? value) => this._session.CanvasSet(this._name, value);

    /// <summary>Clears this canvas. (JS: <c>canvas.clear</c>)</summary>
    public void Clear() => this._session.CanvasClear(this._name);
}
