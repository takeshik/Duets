namespace Duets.Pad;

/// <summary>
/// Host object bound to the <c>pad</c> global in script. Provides DuetsPad host-command
/// operations: session reset, open-text, and editor-text replacement.
/// </summary>
/// <remarks>
/// JS calls are camelCase; Jint maps them to the PascalCase CLR methods below.
/// All calls enqueue a control command on the owning <see cref="DuetsPadSession"/>, which is
/// flushed to subscribers after the current eval or interaction handler completes.
/// The effects are therefore eventually-consistent, not immediate.
/// </remarks>
internal sealed class PadGlobal(DuetsPadSession session)
{
    private readonly DuetsPadSession _session =
        session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>
    /// Requests a session reset (engine, canvas, and timeline). Last-wins within a single eval.
    /// (JS: <c>pad.resetSession</c>)
    /// </summary>
    public void ResetSession() => this._session.RequestResetSession();

    /// <summary>
    /// Requests that a new tab be opened seeded with <paramref name="text"/>.
    /// Every call is delivered; there is no collapse within a single eval.
    /// (JS: <c>pad.openText</c>)
    /// </summary>
    public void OpenText(string text) => this._session.RequestOpenText(text);

    /// <summary>
    /// Requests that the editor content be replaced with <paramref name="text"/>.
    /// Last-wins within a single eval.
    /// (JS: <c>pad.setEditorText</c>)
    /// </summary>
    public void SetEditorText(string text) => this._session.RequestSetEditorText(text);
}
