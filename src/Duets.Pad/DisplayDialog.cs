using Duets.Pad.Dialogs;

namespace Duets.Pad;

/// <summary>
/// Session-bound handle for a modal dialog opened by <c>ui.dialog</c>.
/// </summary>
public sealed class DisplayDialog
{
    private readonly IDialogHost _host;

    internal DisplayDialog(IDialogHost host, Guid id)
    {
        this._host = host ?? throw new ArgumentNullException(nameof(host));
        this.Id =
            id != Guid.Empty
                ? id
                : throw new ArgumentException("Dialog id cannot be empty.", nameof(id));
    }

    internal Guid Id { get; }

    /// <summary>
    /// Gets whether this dialog remains active in its owning DuetsPad session.
    /// </summary>
    public bool IsOpen => this._host.IsDialogOpen(this.Id);

    /// <summary>
    /// Closes this dialog without invoking its user-result callback. Repeated calls are safe.
    /// </summary>
    public void Close() => this._host.CloseDialog(this.Id);
}
