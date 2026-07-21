namespace Duets.Pad.Modals;

internal interface IModalHost
{
    public DisplayModal ShowModal(object? body, Action<ModalResult> onResult, ModalOptions options);

    public bool IsModalOpen(Guid modalId);

    public void CloseModal(Guid modalId);
}
