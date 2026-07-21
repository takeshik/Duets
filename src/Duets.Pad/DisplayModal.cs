using Duets.Pad.Modals;

namespace Duets.Pad;

/// <summary>
/// Session-bound handle for a modal opened by <c>ui.modal</c>.
/// </summary>
public sealed class DisplayModal
{
    private readonly IModalHost _host;

    internal DisplayModal(IModalHost host, Guid id)
    {
        this._host = host ?? throw new ArgumentNullException(nameof(host));
        this.Id =
            id != Guid.Empty
                ? id
                : throw new ArgumentException("Modal id cannot be empty.", nameof(id));
    }

    internal Guid Id { get; }

    /// <summary>
    /// Gets whether this modal remains active in its owning DuetsPad session.
    /// </summary>
    public bool IsOpen => this._host.IsModalOpen(this.Id);

    /// <summary>
    /// Closes this modal without invoking its user-result callback. Repeated calls are safe.
    /// </summary>
    public void Close() => this._host.CloseModal(this.Id);
}
