using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;

namespace KrakenUnlocker.ViewModels.Pages
{
    public partial class SpooferViewModel : ObservableObject
    {
        private readonly ISnackbarService _snackbarService;
        private TimeSpan _snackbarDuration = TimeSpan.FromSeconds(4);
        private Lazy<XboxRestAPI> _xboxRestAPI = new Lazy<XboxRestAPI>(() => new XboxRestAPI(HomeViewModel.XAUTH));

        public SpooferViewModel(ISnackbarService snackbarService)
        {
            _snackbarService = snackbarService;
        }

        public Task OnNavigatedToAsync()
        {
            if (!IsInitialized && HomeViewModel.InitComplete)
                InitializeViewModel();
            return Task.CompletedTask;
        }

        private void InitializeViewModel()
        {
            IsInitialized = true;
        }

        [ObservableProperty] private string _gameName = "";
        [ObservableProperty] private string _gameTitleID = "";
        [ObservableProperty] private string _gamePFN = "";
        [ObservableProperty] private string _gameType = "";
        [ObservableProperty] private string _gameGamepass = "";
        [ObservableProperty] private string _gameDevices = "";
        [ObservableProperty] private string _gameGamerscore = "";
        [ObservableProperty] private string? _gameImage = "pack://application:,,,/Assets/cirno.png";
        [ObservableProperty] private string _gameTime = "";
        [ObservableProperty] private bool _isInitialized = false;
        [ObservableProperty] private string _currentSpoofingID = "";
        [ObservableProperty] private string _newSpoofingID = "";
        [ObservableProperty] private string _spoofingText = "Spoofing Not Started";
        [ObservableProperty] private string _spoofingButtonText = "Start Spoofing";
        private bool SpoofingUpdate = false;
        private bool CurrentlySpoofing = false;
        private GameTitle GameInfoResponse;
        private GameStatsResponse GameStatsResponse;

        [RelayCommand]
        public async Task SpooferButtonClicked()
        {
            if (CurrentlySpoofing)
            {
                SpoofingUpdate = true;
                CurrentlySpoofing = false;
                SpoofingText = "Spoofing Not Started";
                SpoofingButtonText = "Start Spoofing";
                //reset game info
                GameName = "";
                GameTitleID = "";
                GamePFN = "";
                GameType = "";
                GameGamepass = "";
                GameDevices = "";
                GameGamerscore = "";
                GameImage = "pack://application:,,,/Assets/cirno.png";
                GameTime = "";
                HomeViewModel.SpoofingStatus = 0;
                await _xboxRestAPI.Value.StopHeartbeatAsync(HomeViewModel.XUIDOnly);
                return;
            }
            HomeViewModel.SpoofedTitleID = NewSpoofingID;

            if (HomeViewModel.SpoofingStatus == 2)
            {
                HomeViewModel.SpoofingStatus = 1;
                AchievementsViewModel.SpoofingUpdate = true;
            }
            HomeViewModel.SpoofingStatus = 1;
            SpoofGame();
        }

        public async void SpoofGame()
        {
            CurrentSpoofingID = NewSpoofingID;
            GameInfoResponse = await _xboxRestAPI.Value.GetGameTitleAsync(HomeViewModel.XUIDOnly, NewSpoofingID);
            GameStatsResponse = await _xboxRestAPI.Value.GetGameStatsAsync(HomeViewModel.XUIDOnly, NewSpoofingID);

            if (GameInfoResponse == null || GameStatsResponse == null || !GameInfoResponse.Titles.Any())
            {
                _snackbarService.Show("Error: Unable to acquire game info or stats",
                    $"The game info was invalid.",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }

            try
            {
                GameName = GameInfoResponse.Titles[0].Name;
                GameImage = !string.IsNullOrEmpty(GameInfoResponse.Titles[0].DisplayImage.ToString()) ? GameInfoResponse.Titles[0].DisplayImage.ToString() : "pack://application:,,,/Assets/cirno.png";
                GameTitleID = GameInfoResponse.Titles[0].TitleId;
                GamePFN = GameInfoResponse.Titles[0].Pfn;
                GameType = GameInfoResponse.Titles[0].Type;
                GameGamepass = GameInfoResponse.Titles[0].GamePass?.IsGamePass.ToString() ?? "No";
                GameDevices = "";
                foreach (var device in GameInfoResponse.Titles[0].Devices)
                {
                    GameDevices += device.ToString() + ", ";
                }
                if (GameDevices.Length > 2) GameDevices = GameDevices.Remove(GameDevices.Length - 2);

                GameGamerscore = GameInfoResponse.Titles[0].Achievement?.CurrentGamerscore.ToString() +
                                 " / " + GameInfoResponse.Titles[0].Achievement?.TotalGamerscore.ToString();
                try
                {
                    var timePlayed = TimeSpan.FromMinutes(Convert.ToDouble(GameStatsResponse.StatListsCollection[0].Stats[0].Value));
                    GameTime = $"{timePlayed.Days}d {timePlayed.Hours}h {timePlayed.Minutes}m";
                }
                catch
                {
                    GameTime = "Unknown";
                }

            }
            catch
            {
                GameName = "";
                _snackbarService.Show("Error: Invalid TitleID",
                    $"The TitleID entered is invalid or does not return information from the API",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }

            SpoofingUpdate = true;
            CurrentlySpoofing = true;
            SpoofingButtonText = "Stop Spoofing";
            SpoofingText = $"Spoofing {GameInfoResponse.Titles[0].Name}";
            await Task.Run(() => Spoofing());

        }

        public async Task Spoofing()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            SpoofingText = "Spoofing started...";
            await _xboxRestAPI.Value.SendHeartbeatAsync(HomeViewModel.XUIDOnly, CurrentSpoofingID);
            var lastHeartbeat = DateTime.UtcNow;
            SpoofingUpdate = false;
            while (!SpoofingUpdate)
            {
                await Task.Delay(1000);
                if (SpoofingUpdate)
                {
                    HomeViewModel.SpoofingStatus = 0;
                    HomeViewModel.SpoofedTitleID = "0";
                    break;
                }
                    SpoofingText = $"Spoofing {GameInfoResponse.Titles[0].Name} For: {stopwatch.Elapsed.ToString(@"hh\:mm\:ss")}";
                if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds >= 300)
                {
                    await _xboxRestAPI.Value.SendHeartbeatAsync(HomeViewModel.XUIDOnly, CurrentSpoofingID);
                    lastHeartbeat = DateTime.UtcNow;
                }
            }
        }
    }
}
