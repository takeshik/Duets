namespace Duets.Pad.Dialogs;

internal interface IDialogHost
{
    public DisplayDialog ShowDialog(
        object? body,
        Action<DialogResult> onResult,
        DialogOptions options
    );

    public bool IsDialogOpen(Guid dialogId);

    public void CloseDialog(Guid dialogId);
}
