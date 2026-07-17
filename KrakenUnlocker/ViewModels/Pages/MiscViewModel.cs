using HtmlAgilityPack;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Diagnostics;
using System.DirectoryServices;
using System.IO;
using System.Text;
using System.Windows.Input;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;


namespace KrakenUnlocker.ViewModels.Pages
{
    public partial class MiscViewModel : ObservableObject// , INavigationAware
    {
        private readonly IContentDialogService _contentDialogService;
        private readonly ISnackbarService _snackbarService;
        private TimeSpan _snackbarDuration = TimeSpan.FromSeconds(4);
        private Lazy<XboxRestAPI> _xboxRestAPI = new Lazy<XboxRestAPI>(() => new XboxRestAPI(HomeViewModel.XAUTH));



        public MiscViewModel(ISnackbarService snackbarService, IContentDialogService contentDialogService)
        {
            _snackbarService = snackbarService;
            _contentDialogService = contentDialogService;
        }

        public Task OnNavigatedToAsync()
        {
            if (!IsInitialized && HomeViewModel.InitComplete)
                InitializeViewModel();
 return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() { return Task.CompletedTask; }

        private void InitializeViewModel()
        {
            IsInitialized = true;

        }

        [ObservableProperty] private bool _isInitialized = false;

        #region GameSearch
        [ObservableProperty] private List<GameItem> _tSearchResults = new List<GameItem>();
        [ObservableProperty] private List<string> _tSearchTitleNames = new List<string>();
        [ObservableProperty] private string _tSearchText = "";
        [ObservableProperty] private string _tSearchGameName = "Name: ";
        [ObservableProperty] private string _tSearchGameTitleID = "";
        [ObservableProperty] private string _tSearchGameTitleBased = "Unknown";

        private string GetDatabasePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XAU", "TitleSearch", "xbox_games.db");
        }

        [RelayCommand]
        public async Task SearchGame()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TSearchText))
                {
                    TSearchTitleNames = new List<string>();
                    TSearchResults = new List<GameItem>();
                    return;
                }

                string dbPath = GetDatabasePath();

                if (!File.Exists(dbPath))
                {
                    _snackbarService.Show("Error", "Game database not found. Please wait for it to download.",
                        ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                var results = await Task.Run(() => SearchGamesInDatabase(dbPath, TSearchText));

                if (!results.Any())
                {
                    _snackbarService.Show("Error", $"No results were found for '{TSearchText}'",
                        ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    TSearchTitleNames = new List<string>();
                    TSearchResults = new List<GameItem>();
                    return;
                }

                results = results.OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase).ToList();

                TSearchResults = results;
                TSearchTitleNames = results.Select(game => game.Title).ToList();
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", $"Search failed: {ex.Message}",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                TSearchTitleNames = new List<string>();
                TSearchResults = new List<GameItem>();
            }
        }

        private List<GameItem> SearchGamesInDatabase(string dbPath, string searchText)
        {
            var results = new List<GameItem>();

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            // Search for games that contain the search text (case-insensitive)
            string sql = @"
                    SELECT title, titleId, isTitleBased 
                    FROM games 
                    WHERE title LIKE @searchText 
                    ORDER BY title COLLATE NOCASE
                    LIMIT 100";

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@searchText", $"%{searchText}%");

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new GameItem
                {
                    Title = reader.GetString("title"),
                    TitleId = reader.GetString("titleId"),
                    IsTitleBased = reader.GetInt32("isTitleBased") == 1
                });
            }

            return results;
        }

        public void DisplayGameInfo(int index)
        {
            try
            {
                if (TSearchResults == null || TSearchResults.Count <= index || index < 0)
                {
                    _snackbarService.Show("Error", "No game found at the selected index.",
                        ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                var selectedGame = TSearchResults[index];

                TSearchGameName = selectedGame.Title;
                TSearchGameTitleID = selectedGame.TitleId;
                TSearchGameTitleBased = $"{(selectedGame.IsTitleBased ? "True" : "False")}";

            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", "Failed to display game info.",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
        }

        #endregion

        #region GamertagSearch
        [ObservableProperty] private string _gamertag = "";
        [ObservableProperty] private string _gamertagName = "";
        [ObservableProperty] private string _gamertagImage = "pack://application:,,,/Assets/cirno.png";
        [ObservableProperty] private string _gamertagScore = "";
        [ObservableProperty] private string _gamertagXuid;
        [ObservableProperty] private bool _excludeZeroGamerscoreGames;
        [ObservableProperty] private bool _excludeXbox360Games;
        [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<GamertagGameItem> _gamertagGames = new();
        [ObservableProperty] private bool _isLoadingGames;

        public class GamertagGameItem
        {
            public string Title { get; set; } = "";
            public string TitleId { get; set; } = "";
            public string Gamerscore { get; set; } = "";
            public string Progress { get; set; } = "";
            public string Devices { get; set; } = "";
        }

        [RelayCommand]
        public async Task SearchGamertag()
        {
            if (string.IsNullOrWhiteSpace(Gamertag))
            {
                _snackbarService.Show("Error", "Please enter a valid gamertag.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }

            if (!HomeViewModel.InitComplete || string.IsNullOrEmpty(HomeViewModel.XAUTH))
            {
                _snackbarService.Show("Not logged in", "Sign in first before searching gamertags.", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), _snackbarDuration);
                return;
            }

            var profileData = await _xboxRestAPI.Value.GetGamertagProfileAsync(Gamertag) ?? new JObject();
            var profileUsers = profileData["profileUsers"]?.FirstOrDefault();
            if (profileUsers == null)
            {
                _snackbarService.Show("Error", "Failed to fetch gamertag information.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }

            GamertagXuid = profileUsers["id"]?.ToString() ?? string.Empty;
            GamertagName = profileUsers["settings"]?.FirstOrDefault(setting => setting["id"]?.ToString() == "Gamertag")?["value"]?.ToString() ?? "Unknown";
            GamertagScore = profileUsers["settings"]?.FirstOrDefault(setting => setting["id"]?.ToString() == "Gamerscore")?["value"]?.ToString() ?? "0";
            GamertagImage = profileUsers["settings"]?.FirstOrDefault(setting => setting["id"]?.ToString() == "GameDisplayPicRaw")?["value"]?.ToString()?.Replace("&mode=Padding", "") ?? string.Empty;

            // Load games in background
            _ = LoadGamertagGamesAsync();
        }

        private async Task LoadGamertagGamesAsync()
        {
            if (string.IsNullOrWhiteSpace(GamertagXuid)) return;
            IsLoadingGames = true;
            GamertagGames.Clear();
            try
            {
                var gamesResponse = await _xboxRestAPI.Value.GetGamesListAsync(GamertagXuid);
                if (gamesResponse?.Titles == null) return;

                foreach (var title in gamesResponse.Titles)
                {
                    if (ExcludeZeroGamerscoreGames && title.Achievement?.TotalGamerscore == 0) continue;
                    if (ExcludeXbox360Games && title.Devices != null && title.Devices.Contains("Xbox360")) continue;

                    GamertagGames.Add(new GamertagGameItem
                    {
                        Title      = title.Name ?? "",
                        TitleId    = title.TitleId ?? "",
                        Gamerscore = $"{title.Achievement?.CurrentGamerscore ?? 0} / {title.Achievement?.TotalGamerscore ?? 0}",
                        Progress   = $"{title.Achievement?.ProgressPercentage ?? 0}%",
                        Devices    = title.Devices != null ? string.Join(", ", title.Devices) : ""
                    });
                }
            }
            catch { }
            finally { IsLoadingGames = false; }
        }

        public async Task ExportToCsvAsync()
        {
            if (string.IsNullOrWhiteSpace(GamertagXuid))
            {
                _snackbarService.Show("Error", "Search for a user first.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                return;
            }
            try
            {
                _snackbarService.Show("Fetching Games", "Trying to get games. This may take a moment depending on the number of games the user has.", ControlAppearance.Primary, new SymbolIcon(SymbolRegular.XboxController24), _snackbarDuration);
                var gamesResponse = await _xboxRestAPI.Value.GetGamesListAsync(GamertagXuid);

                if (gamesResponse == null || gamesResponse.Titles == null)
                {
                    await Task.Delay(2500);
                    _snackbarService.Show("Error", "Failed to fetch games list.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                if (gamesResponse.Titles.Count == 0)
                {
                    await Task.Delay(2500);
                    _snackbarService.Show("No Titles Found", "No games found for this user. This could be due to user privacy settings or other reasons.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("\"Title ID\",\"Title\",\"CurrentAchievements\",\"Gamerscore\",\"Progress\",\"Devices\",\"Genres\"");

                foreach (var title in gamesResponse.Titles)
                {
                    if (ExcludeZeroGamerscoreGames && title.Achievement.TotalGamerscore == 0)
                    {
                        continue;
                    }

                    if (ExcludeXbox360Games && title.Devices != null && title.Devices.Contains("Xbox360"))
                    {
                        continue;
                    }

                    var titleName = title.Name.Replace("\"", "\"\"");
                    var devices = title.Devices != null ? string.Join(", ", title.Devices).Replace("\"", "\"\"") : string.Empty;
                    var genres = title.Detail?.Genres != null ? string.Join(", ", title.Detail.Genres).Replace("\"", "\"\"") : string.Empty;

                    sb.AppendLine($"\"{title.TitleId}\",\"{titleName}\",\"{title.Achievement.CurrentAchievements}\",\"{title.Achievement.CurrentGamerscore}/{title.Achievement.TotalGamerscore}\",\"{title.Achievement.ProgressPercentage}\",\"{devices}\",\"{genres}\"");
                }

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = $"{GamertagXuid}.csv"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    await Task.Run(() =>
                    {
                        File.WriteAllText(saveFileDialog.FileName, sb.ToString());
                    });

                    _snackbarService.Show("Success", "Games list exported successfully.", ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), _snackbarDuration);
                }
                else
                {
                    _snackbarService.Show("Cancelled", "Game export was not completed", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.Warning24), _snackbarDuration);
                }
            }
            catch (Exception ex)
            {
                _snackbarService.Show("Error", "Failed to export games list: " + ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), _snackbarDuration);
            }
        }
        #endregion
    }
}
