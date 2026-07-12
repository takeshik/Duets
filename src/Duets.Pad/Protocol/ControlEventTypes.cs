namespace Duets.Pad.Protocol;

/// <summary>
/// Canonical SSE event-type discriminators for the <c>control.*</c> namespace.
/// Concrete op names (e.g. <c>reset</c>, <c>openText</c>) are added here as each
/// command is introduced.
/// </summary>
internal static class ControlEventTypes
{
    /// <summary>Namespace prefix shared by all control events.</summary>
    public const string Prefix = "control.";

    /// <summary>Op name for <c>pad.resetSession()</c>: resets the engine, canvas, and timeline.</summary>
    public const string Reset = "reset";

    /// <summary>Op name for <c>pad.openText(text)</c>: opens a new tab with the given text handed off as the initial content.</summary>
    public const string OpenText = "openText";

    /// <summary>Op name for <c>pad.setEditorText(text)</c>: replaces the editor content with the given text.</summary>
    public const string SetEditorText = "setEditorText";

    /// <summary>
    /// Returns the full SSE event-type string for <paramref name="op"/>, e.g.
    /// <c>Make("reset")</c> → <c>"control.reset"</c>.
    /// </summary>
    public static string Make(string op) => Prefix + op;
}
