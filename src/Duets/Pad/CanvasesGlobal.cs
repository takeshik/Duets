namespace Duets.Pad;

/// <summary>
/// Host object bound to the <c>canvases</c> global in script. Provides getOrAdd access to
/// named canvases via <see cref="Get"/>.
/// </summary>
/// <remarks>
/// JS calls are camelCase; Jint maps them to the PascalCase CLR methods below.
/// Canvas creation is lazy — the first <c>canvases.get(name)</c> call creates the canvas.
/// Access is serialized by the eval semaphore in <see cref="DuetsPadSession"/>, so no
/// additional locking is needed here.
/// </remarks>
internal sealed class CanvasesGlobal(DuetsPadSession session)
{
    private readonly DuetsPadSession _session =
        session ?? throw new ArgumentNullException(nameof(session));

    private readonly Dictionary<string, CanvasGlobal> _canvases = [];

    /// <summary>
    /// Returns the <see cref="CanvasGlobal"/> for the given <paramref name="name"/>, creating it
    /// on first access. (JS: <c>canvases.get(name)</c>)
    /// </summary>
    /// <param name="name">The canvas name. Must be non-empty.</param>
    public CanvasGlobal Get(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Canvas name must be non-empty.", nameof(name));
        }

        if (!this._canvases.TryGetValue(name, out var canvas))
        {
            canvas = new CanvasGlobal(this._session, name);
            this._canvases[name] = canvas;
        }

        return canvas;
    }
}
