using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;
using KrakenUnlocker.Views.Pages;
namespace KrakenUnlocker.ViewModels.Pages
{
    public partial class GamesViewModel(ISnackbarService snackbarService, INavigationService navigationService) : ObservableObject// , INavigationAware, INotifyPropertyChanged
    {
        [ObservableProperty] private string _xuidOverride = "0";
        [ObservableProperty] private ObservableCollection<Game> _games = new ObservableCollection<Game>();
        [ObservableProperty] private string _searchLabel = "Search 0 Games";
        [ObservableProperty] private GridLength _gamesListHeight = new GridLength(0, GridUnitType.Star);
        [ObservableProperty] private GridLength _loadingHeight = new GridLength(1, GridUnitType.Star);
        [ObservableProperty] private double _loadingSize = 200;
        [ObservableProperty] private string _searchText = "";
        [ObservableProperty] private List<string> _filterOptions = new List<string>() { "All", "Xbox One/Series", "Xbox 360", "PC", "Incomplete Games", "Win32" };
        [ObservableProperty] private int _filterIndex = 0;
        [ObservableProperty] private bool _isInitialized = false;
        [ObservableProperty] private bool _isLoading = false;

        TitlesList GamesResponse = new TitlesList();

        public GamesViewModel(ISnackbarService snackbarService, INavigationService navigationService, bool unused = false)
            : this(snackbarService, navigationService)
        {
            // Auto-load games whenever login completes
            HomeViewModel.LoginCompleted += async () => await ReloadAfterLoginAsync();
        }

        public class Game
        {
            public required string Title { get; set; }
            public required string Image { get; set; }
            public required string Gamerscore { get; set; }
            public required string CurrentAchievements { get; set; }
            public required string Progress { get; set; }
            public required string Index { get; set; }

            // Precomputed once at build time so filtering stays allocation-free
            internal string TitleLower = "";
            internal bool IsXboxConsole;
            internal bool IsPC;
            internal bool IsXbox360;
            internal bool IsWin32;
            internal bool IsIncomplete;
        }

        // TODO: this needs to be updated if language changes
        private Lazy<XboxRestAPI> _xboxRestAPI = new Lazy<XboxRestAPI>(() => new XboxRestAPI(HomeViewModel.XAUTH));

        private readonly ISnackbarService _snackbarService = snackbarService;
        private TimeSpan _snackbarDuration = TimeSpan.FromSeconds(4);

        public async Task OnNavigatedToAsync()
        {
            if (!IsInitialized && HomeViewModel.InitComplete)
                await InitializeViewModel();
        }

        public Task OnNavigatedFromAsync()
        {
            return Task.CompletedTask;
        }

        private async Task InitializeViewModel()
        {
            XuidOverride = HomeViewModel.XUIDOnly;
            IsInitialized = true;
            await GetGamesList();
        }

        // Called externally when login completes so games load immediately
        public async Task ReloadAfterLoginAsync()
        {
            IsInitialized = false;
            await InitializeViewModel();
        }

        [RelayCommand]
        private async Task GetGamesList()
        {
            if (KrakenUnlocker.Services.UpdateBlocker.IsHardBlocked)
            {
                _snackbarService.Show("Update Required", "You must update KrakenXboxUnlocker to use any features.",
                    ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }
            if (string.IsNullOrWhiteSpace(XuidOverride) || string.IsNullOrEmpty(XuidOverride))
            {
                _snackbarService.Show(
                    "Error",
                    "XUID Override cannot be empty.",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24),
                    _snackbarDuration
                );
                return;
            }

            Games.Clear();
            LoadingStart();
            // JSON deserialization runs on the threadpool (see GetGamesListAsync),
            // so the UI thread stays free while thousands of titles are parsed.
            GamesResponse = await _xboxRestAPI.Value.GetGamesListAsync(XuidOverride) ?? new TitlesList();

            // Build into a plain List first, then assign a single ObservableCollection.
            // Constructing it from a pre-built list copies the backing storage in bulk
            // (no per-item CollectionChanged events), and the assignment raises exactly
            // one PropertyChanged -> the ListBox re-binds once. This is O(1) UI work
            // instead of O(n) notifications from Add()-ing thousands of items one at a
            // time, which is what made the initial load slow for large libraries.
            var list = new List<Game>(GamesResponse.Titles.Count);
            BuildGamesInto(list);
            Games = new ObservableCollection<Game>(list);

            SearchLabel = $"Search {Games.Count} Games";
            ApplyGamesFilter();
            LoadingEnd();
        }

        private void BuildGamesInto(List<Game> list)
        {
            for (int i = 0; i < GamesResponse.Titles.Count; i++)
            {
                var title = GamesResponse.Titles[i];
                var achievement = title.Achievement;

                var EditedImage = !string.IsNullOrEmpty(title.DisplayImage) ? title.DisplayImage! : "pack://application:,,,/Assets/cirno.png";
                if (EditedImage.Contains("store-images.s-microsoft.com"))
                {
                    EditedImage += "?w=256&h=256&format=jpg";
                }

                // Single pass over the device list instead of 5 separate Contains() scans.
                bool isXboxConsole = false, isPC = false, isXbox360 = false, isWin32 = false;
                foreach (var device in title.Devices)
                {
                    switch (device)
                    {
                        case "XboxSeries":
                        case "XboxOne":
                            isXboxConsole = true;
                            break;
                        case "PC":
                            isPC = true;
                            break;
                        case "Xbox360":
                            isXbox360 = true;
                            break;
                        case "Win32":
                            isWin32 = true;
                            break;
                    }
                }

                // ProgressPercentage is already a double; no need to round-trip via string.
                var progress = achievement?.ProgressPercentage ?? 0;
                var name = title.Name ?? "";

                list.Add(new Game()
                {
                    Title = name,
                    CurrentAchievements = (achievement?.CurrentAchievements ?? 0).ToString(),
                    Gamerscore = (achievement?.CurrentGamerscore ?? 0) + "/" +
                                 (achievement?.TotalGamerscore ?? 0),
                    Progress = progress.ToString(),
                    Image = EditedImage,
                    Index = i.ToString(),
                    TitleLower = name.ToLowerInvariant(),
                    IsXboxConsole = isXboxConsole,
                    IsPC = isPC,
                    IsXbox360 = isXbox360,
                    IsWin32 = isWin32,
                    IsIncomplete = progress < 100
                });
            }
        }

        // Filtering is done entirely through the ICollectionView predicate against the
        // precomputed Game fields, so it is O(n) with no allocations and never rebuilds
        // the bound collection. Virtualization means only visible cards are realized.
        private void ApplyGamesFilter()
        {
            var view = CollectionViewSource.GetDefaultView(Games);
            var filterIndex = FilterIndex;
            var searchLower = (SearchText ?? "").ToLowerInvariant();

            // No filter active -> drop the predicate so the view skips iterating every
            // item entirely. This avoids an O(n) pass on the common initial load and the
            // default "All" view, which matters for libraries with thousands of titles.
            if (filterIndex == 0 && searchLower.Length == 0)
            {
                view.Filter = null;
                return;
            }

            view.Filter = obj =>
            {
                var g = (Game)obj;
                switch (filterIndex)
                {
                    case 1:
                        if (!g.IsXboxConsole) return false;
                        break;
                    case 2:
                        if (!g.IsXbox360) return false;
                        break;
                    case 3:
                        if (!g.IsPC) return false;
                        break;
                    case 4:
                        if (!g.IsIncomplete) return false;
                        break;
                    case 5:
                        if (!g.IsWin32) return false;
                        break;
                }
                if (searchLower.Length > 0 && !g.TitleLower.Contains(searchLower))
                    return false;
                return true;
            };
        }

        public async Task OpenAchievements(string index)
        {
            AchievementsViewModel.TitleID = GamesResponse.Titles[int.Parse(index)].TitleId;
            AchievementsViewModel.IsSelectedGame360 = GamesResponse.Titles[int.Parse(index)].Devices.Contains("Xbox360") || GamesResponse.Titles[int.Parse(index)].Devices.Contains("Mobile");
            AchievementsViewModel.NewGame = true;
            // Use MainWindow navigation instead of WPF-UI nav service (which isn't wired to our custom frame)
            App.Current.Dispatcher.Invoke(() =>
            {
                var mainWindow = App.Current.MainWindow as KrakenUnlocker.Views.Windows.MainWindow;
                var achievementsPage = App.GetService<AchievementsPage>();
                if (mainWindow != null && achievementsPage != null)
                {
                    mainWindow.MainContentFrame.Navigate(achievementsPage);
                    _ = App.GetService<AchievementsViewModel>()?.OnNavigatedToAsync();
                }
            });
            await Task.CompletedTask;
        }

        [RelayCommand]
        public void SearchAndFilterGames()
        {
            if (!IsInitialized)
                return;
            ApplyGamesFilter();
        }

        [RelayCommand]
        public void FilterGames()
        {
            if (!IsInitialized)
            {
                return;
            }
            ApplyGamesFilter();
        }

        public void LoadingStart()
        {
            IsLoading = true;
            LoadingSize = 200;
            GamesListHeight = new GridLength(0, GridUnitType.Star);
            LoadingHeight = new GridLength(1, GridUnitType.Star);
        }

        public void LoadingEnd()
        {
            IsLoading = false;
            GamesListHeight = new GridLength(1, GridUnitType.Star);
            LoadingHeight = new GridLength(0, GridUnitType.Star);
            LoadingSize = 0;
        }

        public void CopyToClipboard(string index)
        {
            var titleid = GamesResponse.Titles[int.Parse(index)].TitleId.ToString();
            var title = GamesResponse.Titles[int.Parse(index)].Name.ToString();
            Clipboard.SetDataObject(GamesResponse.Titles[int.Parse(index)].TitleId.ToString());
            _snackbarService.Show("TitleID Copied", $"Copied the title ID of {title.ToString()} to clipboard\nTitleID: {titleid.ToString()}", ControlAppearance.Success, new SymbolIcon(SymbolRegular.ClipboardCheckmark24), _snackbarDuration);
        }
    }

}
