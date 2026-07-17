using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Wpf.Ui.Contracts;

namespace KrakenUnlocker.Services
{
    /// <summary>
    /// Custom snackbar service that routes all notifications to KrakenToast
    /// instead of WPF-UI's default green/blue snackbar.
    /// </summary>
    public class KrakenSnackbarService : ISnackbarService
    {
        public TimeSpan DefaultTimeOut { get; set; } = TimeSpan.FromSeconds(3);
        public void SetSnackbarPresenter(SnackbarPresenter contentPresenter) { }
        public SnackbarPresenter GetSnackbarPresenter() => new SnackbarPresenter();

        public void Show(string title, string message, ControlAppearance appearance,
            IconElement? icon, TimeSpan timeout)
        {
            var type = appearance switch
            {
                ControlAppearance.Success  => ToastType.Success,
                ControlAppearance.Caution  => ToastType.Warning,
                ControlAppearance.Danger   => ToastType.Error,
                ControlAppearance.Dark     => ToastType.Error,
                _                          => ToastType.Info,
            };
            KrakenToast.Show(title, message, type, (int)timeout.TotalMilliseconds);
        }

        public void Show(string title, ControlAppearance appearance,
            IconElement? icon, TimeSpan timeout)
            => Show(title, "", appearance, icon, timeout);
    }
}
