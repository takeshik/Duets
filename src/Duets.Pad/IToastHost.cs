namespace Duets.Pad;

internal interface IToastHost
{
    public void ShowToast(string message, ToastOptions options);
}
