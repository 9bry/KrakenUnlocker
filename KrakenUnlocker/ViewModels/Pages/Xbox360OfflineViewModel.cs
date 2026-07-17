using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KrakenUnlocker.Xbox360;
using System.Collections.ObjectModel;
using System.IO;

namespace KrakenUnlocker.ViewModels.Pages
{
    public partial class Xbox360OfflineViewModel : ObservableObject
    {
        // ── Package ───────────────────────────────────────────────────────────────
        [ObservableProperty] private string _packagePath = "";
        [ObservableProperty] private string _packageType = "";
        [ObservableProperty] private string _displayName = "";
        [ObservableProperty] private string _description = "";
        [ObservableProperty] private string _titleId = "";
        [ObservableProperty] private bool _packageLoaded;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _statusText = "Open a CON/LIVE/PIRS package file to get started.";
        [ObservableProperty] private System.Windows.Media.ImageSource? _thumbnailImage;
        [ObservableProperty] private ObservableCollection<StfsFileEntry> _fileEntries = new();
        [ObservableProperty] private StfsFileEntry? _selectedFile;

        // ── GPD ───────────────────────────────────────────────────────────────────
        [ObservableProperty] private string _gpdPath = "";
        [ObservableProperty] private bool _gpdLoaded;
        [ObservableProperty] private ObservableCollection<GpdAchievement> _achievements = new();
        [ObservableProperty] private string _gpdStatus = "Open a .gpd file to view and edit achievement flags.";
        [ObservableProperty] private int _unlockedCount;
        [ObservableProperty] private int _totalCount;

        private StfsPackage? _package;
        private string _localSavePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        // ── Package commands ──────────────────────────────────────────────────────
        [RelayCommand]
        private async Task OpenPackageAsync()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open STFS Package",
                Filter = "STFS Packages (CON/LIVE/PIRS)|*.*|All files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            IsBusy = true;
            StatusText = "Opening package...";

            await Task.Run(() =>
            {
                try
                {
                    _package?.Dispose();
                    _package = StfsPackage.Open(dlg.FileName);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        PackagePath   = _package.Metadata.FilePath;
                        PackageType   = _package.Metadata.PackageType.ToString();
                        DisplayName   = _package.Metadata.DisplayName;
                        Description   = _package.Metadata.Description;
                        TitleId       = _package.Metadata.TitleId == 0 ? "" : $"0x{_package.Metadata.TitleId:X8}";
                        PackageLoaded = true;

                        if (_package.Metadata.ThumbnailImage != null)
                        {
                            using var ms = new System.IO.MemoryStream(_package.Metadata.ThumbnailImage);
                            var img = new System.Windows.Media.Imaging.BitmapImage();
                            img.BeginInit();
                            img.StreamSource = ms;
                            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            img.EndInit();
                            ThumbnailImage = img;
                        }

                        FileEntries.Clear();
                        foreach (var e in _package.RootListing)
                            FileEntries.Add(e);

                        StatusText = $"Loaded {_package.Metadata.PackageType} — {_package.RootListing.Count} entries";
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusText = $"Error: {ex.Message}";
                    });
                }
            });

            IsBusy = false;
        }

        [RelayCommand]
        private void SaveMetadata()
        {
            if (_package == null) return;
            try
            {
                _package.Metadata.DisplayName = DisplayName;
                _package.Metadata.Description = Description;
                _package.SaveMetadata();
                _package.Rehash();
                StatusText = "Metadata saved and rehashed.";
            }
            catch (Exception ex)
            {
                StatusText = $"Save error: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ExtractFileAsync()
        {
            if (SelectedFile == null || SelectedFile.IsDirectory) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = SelectedFile.Name,
                Title = "Save extracted file"
            };
            if (dlg.ShowDialog() != true) return;

            IsBusy = true;
            StatusText = $"Extracting {SelectedFile.Name}...";

            await Task.Run(() =>
            {
                try
                {
                    _package!.ExtractFile(SelectedFile, dlg.FileName);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        StatusText = $"Extracted to {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        StatusText = $"Extract error: {ex.Message}");
                }
            });

            IsBusy = false;
        }

        [RelayCommand]
        private void ExtractAllFiles()
        {
            if (_package == null) return;
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder to extract all files"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            IsBusy = true;
            Task.Run(() =>
            {
                int count = 0;
                foreach (var entry in _package.RootListing.Where(e => !e.IsDirectory))
                {
                    try
                    {
                        _package.ExtractFile(entry, Path.Combine(dlg.SelectedPath, entry.Name));
                        count++;
                    }
                    catch { }
                }
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusText = $"Extracted {count} files to {dlg.SelectedPath}";
                    IsBusy = false;
                });
            });
        }

        // ── GPD commands ──────────────────────────────────────────────────────────
        [RelayCommand]
        private async Task OpenGpdAsync()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open GPD File",
                Filter = "GPD Files (*.gpd)|*.gpd|All files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            IsBusy = true;
            GpdStatus = "Loading GPD...";

            await Task.Run(() =>
            {
                try
                {
                    var achievements = GpdReader.ReadAchievements(dlg.FileName);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        GpdPath = dlg.FileName;
                        Achievements.Clear();
                        foreach (var a in achievements.OrderBy(x => x.Id))
                            Achievements.Add(a);
                        TotalCount    = achievements.Count;
                        UnlockedCount = achievements.Count(a => a.IsUnlocked);
                        GpdLoaded = true;
                        GpdStatus = $"Loaded {achievements.Count} achievements — {UnlockedCount} unlocked";
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        GpdStatus = $"Error: {ex.Message}");
                }
            });

            IsBusy = false;
        }

        [RelayCommand]
        private void ToggleAchievement(GpdAchievement? ach)
        {
            if (ach == null || string.IsNullOrEmpty(GpdPath)) return;
            try
            {
                bool newState = !ach.IsUnlocked;
                GpdReader.SetUnlocked(GpdPath, ach, newState);
                ach.IsUnlocked = newState;
                UnlockedCount = Achievements.Count(a => a.IsUnlocked);
                GpdStatus = $"{ach.Name} — {(newState ? "Unlocked ✓" : "Locked ✕")}";

                // Refresh UI
                var idx = Achievements.IndexOf(ach);
                if (idx >= 0)
                {
                    Achievements.RemoveAt(idx);
                    Achievements.Insert(idx, ach);
                }
            }
            catch (Exception ex)
            {
                GpdStatus = $"Error: {ex.Message}";
            }
        }

        [RelayCommand]
        private void UnlockAllAchievements()
        {
            if (string.IsNullOrEmpty(GpdPath)) return;
            foreach (var a in Achievements.Where(x => !x.IsUnlocked).ToList())
                ToggleAchievement(a);
            GpdStatus = $"All {Achievements.Count} achievements unlocked.";
        }
    }
}
