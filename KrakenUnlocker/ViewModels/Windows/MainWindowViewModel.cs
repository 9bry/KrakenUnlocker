using System.Collections.ObjectModel;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;

namespace KrakenUnlocker.ViewModels.Windows
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IContentDialogService _contentDialogService;
        private readonly INavigationService _navigationService;
        private readonly DateTime _startTime = DateTime.Now;

        public MainWindowViewModel(
            IContentDialogService contentDialogService,
            INavigationService navigationService)
        {
            _contentDialogService = contentDialogService;
            _navigationService = navigationService;
        }

        [ObservableProperty] private string _applicationTitle = AppBranding.Name;
        [ObservableProperty] private string _activeSection = "Command";
        [ObservableProperty] private string _activeSectionHint = "Live profile & session";

        public ObservableCollection<ShellNavItem> PrimaryNav { get; } =
        [
            new("Command", "Live profile & session", SymbolRegular.Home24, typeof(Views.Pages.HomePage)),
            new("Library", "Your title collection", SymbolRegular.Games24, typeof(Views.Pages.GamesPage)),
            new("Trophies", "Unlock & manage", SymbolRegular.Trophy24, typeof(Views.Pages.AchievementsPage)),
            new("Pipeline", "Auto unlock queue", SymbolRegular.TaskListLtr24, typeof(Views.Pages.QueuePage)),
            new("360 Vault", "Xbox 360 console manager", SymbolRegular.XboxConsole24, typeof(Views.Pages.Xbox360Page)),
            new("Stats Lab", "Title stat editor", SymbolRegular.DataHistogram24, typeof(Views.Pages.StatsPage)),
            new("Arsenal", "Spoofer & utilities", SymbolRegular.MoreCircle24, typeof(Views.Pages.MiscPage)),
        ];

        public ObservableCollection<ShellNavItem> UtilityNav { get; } =
        [
            new("Config", "Preferences & API", SymbolRegular.Settings24, typeof(Views.Pages.SettingsPage)),
            new("About", "Creator & links", SymbolRegular.Info24, typeof(Views.Pages.InfoPage)),
        ];

        [RelayCommand]
        private void NavigateTo(ShellNavItem? item)
        {
            if (item?.TargetPage is null) return;

            foreach (var nav in PrimaryNav.Concat(UtilityNav))
                nav.IsActive = nav == item;

            ActiveSection = item.Label;
            ActiveSectionHint = item.Subtitle;
            _navigationService.Navigate(item.TargetPage);
        }

        public async Task ShowErrorDialog(Exception exception)
        {
            var uptime = DateTime.Now - _startTime;
            var output = $"""
                {AppBranding.Name} — Error Report
                =================================
                Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}
                OS: {Environment.OSVersion.Version}
                Uptime: {uptime}
                =================================
                {exception}
                """;

            var result = await _contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
            {
                Title = $"{AppBranding.ShortName} ERROR",
                Content = "Something went wrong.\nCopy the error and report it on Discord.",
                PrimaryButtonText = "Copy Error",
                CloseButtonText = "Close",
            });

            if (result == ContentDialogResult.Primary)
                Clipboard.SetDataObject(output);
        }
    }

    public partial class ShellNavItem : ObservableObject
    {
        public ShellNavItem(string label, string subtitle, SymbolRegular icon, Type targetPage)
        {
            Label = label;
            Subtitle = subtitle;
            Icon = icon;
            TargetPage = targetPage;
        }

        public string Label { get; }
        public string Subtitle { get; }
        public SymbolRegular Icon { get; }
        public Type TargetPage { get; }

        [ObservableProperty] private bool _isActive;
    }
}
