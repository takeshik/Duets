namespace Duets.Pad;

/// <summary>
/// Host object bound to the <c>pad</c> global in script. Provides session identity, server-held
/// Editor text, and DuetsPad host-command operations.
/// </summary>
/// <remarks>
/// JS calls are camelCase; Jint maps them to the PascalCase CLR methods below.
/// Reset and open-text calls enqueue control commands on the owning
/// <see cref="DuetsPadSession"/>. Editor text reads and writes access session state directly.
/// </remarks>
internal sealed class PadGlobal(DuetsPadSession session)
{
    private readonly DuetsPadSession _session =
        session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>Gets the current DuetsPad session identifier. (JS: <c>pad.sessionId</c>)</summary>
    public string SessionId => this._session.Id.ToString();

    /// <summary>Gets or replaces the last committed Editor text. (JS: <c>pad.editorText</c>)</summary>
    public string EditorText
    {
        get => this._session.GetEditorText();
        set => this._session.ReplaceEditorTextFromScript(value);
    }

    /// <summary>
    /// Requests a session reset (engine, canvas, and timeline). Last-wins within a single eval.
    /// (JS: <c>pad.resetSession</c>)
    /// </summary>
    public void ResetSession() => this._session.RequestResetSession();

    /// <summary>
    /// Requests that a new tab be opened with <paramref name="text"/> handed off as the initial content.
    /// Every call is delivered; there is no collapse within a single eval.
    /// (JS: <c>pad.openText</c>)
    /// </summary>
    public void OpenText(string text) => this._session.RequestOpenText(text);
}
