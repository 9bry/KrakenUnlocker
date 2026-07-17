using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using KrakenUnlocker.Models;

namespace KrakenUnlocker.ViewModels.Pages
{
    public partial class InfoViewModel : ObservableObject// , INavigationAware
    {
        private bool _isInitialized = false;
        [ObservableProperty] private string? _toolVersion;

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();
 return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() { return Task.CompletedTask; }

        private void InitializeViewModel()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version != null ? $"{version.Major}.{version.Minor}" : "1.0";
            ToolVersion = $"v{versionStr}";
            _isInitialized = true;
        }

        [RelayCommand]
        public void OpenTelegramUrl()
        {
            OpenUrl(OpenableLinks.Telegram);
        }

        [RelayCommand]
        public void OpenDiscordUrl()
        {
            OpenUrl(OpenableLinks.Discord);
        }

        [RelayCommand]
        public void OpenGithubUserUrl()
        {
            OpenUrl(OpenableLinks.GitHubUserUrl);
        }

        [RelayCommand]
        public void OpenInstagramUrl()
        {
            OpenUrl(OpenableLinks.Instagram);
        }

        private static void OpenUrl(string url)
        {
            var sInfo = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
            System.Diagnostics.Process.Start(sInfo);
        }
    }
}
