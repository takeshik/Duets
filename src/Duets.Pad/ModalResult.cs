namespace Duets.Pad;

/// <summary>
/// Describes how a user completed a <c>ui.modal</c> interaction.
/// </summary>
public sealed class ModalResult
{
    internal ModalResult(string reason, string? actionId)
    {
        this.Reason = reason;
        this.ActionId = actionId;
    }

    /// <summary>
    /// Gets <c>"action"</c> for an explicit footer action or <c>"dismiss"</c> for a modal
    /// dismissal gesture.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the selected action id, or <see langword="null"/> for an unmapped dismissal.
    /// </summary>
    public string? ActionId { get; }
}
