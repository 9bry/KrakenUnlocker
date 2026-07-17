using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KrakenUnlocker.Services;

namespace KrakenUnlocker.ViewModels.Pages
{
    public partial class LicenseViewModel : ObservableObject
    {
        private DispatcherTimer? _countdownTimer;

        [ObservableProperty] private string _email = "";
        [ObservableProperty] private string _code = "";
        [ObservableProperty] private bool _codeSent;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private bool _isPremium;
        [ObservableProperty] private string _currentEmail = "";
        [ObservableProperty] private string _expiresText = "";
        [ObservableProperty] private string _daysLeftText = "";
        [ObservableProperty] private string _sessionCountdown = "";
        [ObservableProperty] private bool _isLifetime;
        [ObservableProperty] private bool _showLogin = true;
        [ObservableProperty] private string _restoreStatus = "Log in with your email and code";
        [ObservableProperty] private bool _restoreIsError = true;

        public LicenseViewModel()
        {
            LicenseService.StateChanged += OnStateChanged;
        }

        private void OnStateChanged()
        {
            App.Current.Dispatcher.Invoke(SyncState);
        }

        private void SyncState()
        {
            IsPremium = LicenseService.IsPremium;
            ShowLogin = !LicenseService.IsPremium;
            CurrentEmail = LicenseService.CurrentEmail ?? "";
            ExpiresText = LicenseService.ExpiresAt.HasValue
                ? $"Session valid until {LicenseService.ExpiresAt.Value:h:mm tt}"
                : "";
            DaysLeftText = LicenseService.IsLifetime ? "♾ Lifetime Access"
                : LicenseService.DaysLeft > 0 ? $"{LicenseService.DaysLeft} days remaining"
                : "";
            IsLifetime = LicenseService.IsLifetime;
            RestoreStatus = LicenseService.IsPremium ? "Session active (login valid until app is closed)" : "Log in with your email and code";
            RestoreIsError = !LicenseService.IsPremium;
            StartCountdown();
        }

        [RelayCommand]
        private async Task SendCodeAsync()
        {
            if (string.IsNullOrWhiteSpace(Email))
            {
                StatusText = "Enter your email first.";
                return;
            }
            IsBusy = true;
            StatusText = "Sending code...";
            var (ok, error) = await LicenseService.SendCodeAsync(Email.Trim().ToLower());
            if (ok)
            {
                CodeSent = true;
                StatusText = "Code sent to your email. Check inbox and spam.";
            }
            else
            {
                StatusText = string.IsNullOrEmpty(error)
                    ? "No active license found for that email. Purchase access first."
                    : error;
            }
            IsBusy = false;
        }

        private void StartCountdown()
        {
            _countdownTimer?.Stop();
            if (!LicenseService.ExpiresAt.HasValue || IsLifetime)
            {
                SessionCountdown = "";
                return;
            }
            UpdateCountdown();
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, _) => UpdateCountdown();
            _countdownTimer.Start();
        }

        private void UpdateCountdown()
        {
            if (!LicenseService.ExpiresAt.HasValue) { SessionCountdown = ""; return; }
            var remaining = LicenseService.ExpiresAt.Value - DateTime.Now;
            if (remaining.TotalSeconds <= 0)
            {
                SessionCountdown = "Session expired";
                _countdownTimer?.Stop();
                return;
            }
            SessionCountdown = remaining.TotalHours >= 1
                ? $"expires in {remaining.Hours}h {remaining.Minutes}m"
                : $"expires in {remaining.Minutes}m {remaining.Seconds}s";
        }

        [RelayCommand]
        private async Task VerifyCodeAsync()
        {
            if (string.IsNullOrWhiteSpace(Code))
            {
                StatusText = "Enter the 6-digit code.";
                return;
            }
            IsBusy = true;
            StatusText = "Verifying...";
            var (ok, error) = await LicenseService.VerifyCodeAsync(Email.Trim().ToLower(), Code.Trim());
            if (ok)
            {
                StatusText = "Logged in successfully!";
                PremiumUnlocked?.Invoke();
            }
            else
            {
                StatusText = string.IsNullOrEmpty(error) ? "Invalid or expired code. Try again." : error;
            }
            IsBusy = false;
        }

        [RelayCommand]
        private void Logout()
        {
            _countdownTimer?.Stop();
            LicenseService.Logout();
        }

        [RelayCommand]
        private void OpenKofi()
        {
            var sInfo = new System.Diagnostics.ProcessStartInfo("https://ko-fi.com/bryyz") { UseShellExecute = true };
            System.Diagnostics.Process.Start(sInfo);
        }

        public static event Action? PremiumUnlocked;
    }
}
