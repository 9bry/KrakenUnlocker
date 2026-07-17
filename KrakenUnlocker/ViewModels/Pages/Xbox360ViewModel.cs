using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KrakenUnlocker.Xbox360;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Media;
using Wpf.Ui.Common;
using Wpf.Ui.Contracts;
using Wpf.Ui.Controls;

namespace KrakenUnlocker.ViewModels.Pages
{
    public partial class Xbox360FileItem : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _fullPath = "";
        [ObservableProperty] private bool _isDirectory;
        [ObservableProperty] private string _size = "";
        [ObservableProperty] private string _icon = "📄";
        [ObservableProperty] private ObservableCollection<Xbox360FileItem> _children = new();
        [ObservableProperty] private bool _isExpanded;
        public bool HasChildren => IsDirectory;
    }

    public partial class AchievementItem : ObservableObject
    {
        [ObservableProperty] private int _id;
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _description = "";
        [ObservableProperty] private int _gamerscore;
        [ObservableProperty] private bool _isUnlocked;
        [ObservableProperty] private string _status = "🔒 Locked";
        [ObservableProperty] private string _unlockTime = "";
    }

    public partial class Xbox360ViewModel : ObservableObject
    {
        private readonly ISnackbarService _snackbarService;
        private bool _isInitialized;

        // ── Connection ──────────────────────────────────────────────────────────
        [ObservableProperty] private string _consoleName = "Not Connected";
        [ObservableProperty] private string _consoleIp = "—";
        [ObservableProperty] private string _connectionStatus = "Disconnected";
        [ObservableProperty] private System.Windows.Media.Brush _connectionStatusColor = System.Windows.Media.Brushes.Red;
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private string _statusText = "Enter console name or IP above and press Connect, or scan your network.";
        [ObservableProperty] private string _targetConsole = "";

        // ── Discovery ──────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<DiscoveredConsole> _discoveredConsoles = new();

        // ── System Info ─────────────────────────────────────────────────────────
        [ObservableProperty] private string _kernelVersion = "—";
        [ObservableProperty] private string _consoleType = "—";
        [ObservableProperty] private string _currentTitle = "—";

        // ── File Browser ────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<Xbox360FileItem> _driveItems = new();
        [ObservableProperty] private Xbox360FileItem? _selectedFile;
        [ObservableProperty] private string _currentPath = "";
        [ObservableProperty] private string _localSavePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        // ── Offline Profile ─────────────────────────────────────────────────────
        [ObservableProperty] private string _offlineProfilePath = "";
        [ObservableProperty] private string _offlineProfileStatus = "Ready to load profile.";
        [ObservableProperty] private ObservableCollection<string> _offlineProfileGames = new();

        // ── Screenshot ──────────────────────────────────────────────────────────
        [ObservableProperty] private string? _screenshotPath;

        public Xbox360ViewModel(ISnackbarService snackbarService)
        {
            _snackbarService = snackbarService;
        }

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                _isInitialized = true;
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private IntPtr _connHandle = IntPtr.Zero;

        // ── Connect ─────────────────────────────────────────────────────────────
        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(TargetConsole))
            {
                StatusText = "Enter a console name or IP first.";
                return;
            }

            IsBusy = true;
            StatusText = $"Connecting to {TargetConsole}...";

            bool ok = false;
            string name = "", ip = "", kernel = "", ctype = "";
            uint errorHr = 0;
            string errorMsg = "";

            await Task.Run(() =>
            {
                try
                {
                    XbdmBridge.DmSetXboxName(TargetConsole);

                    var nameBuf = new StringBuilder(256);
                    uint nameSize = 256;
                    uint hr = XbdmBridge.DmGetNameOfXbox(nameBuf, ref nameSize, true);

                    if (hr == XbdmBridge.XBDM_NOERR || hr == XbdmBridge.XBDM_CONNECTED)
                    {
                        name = nameBuf.Length > 0 ? nameBuf.ToString() : TargetConsole;

                        XbdmBridge.DmGetAltAddress(out uint addr);
                        var bytes = BitConverter.GetBytes(addr);
                        ip = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";

                        // Open full debug channel connection
                        uint connHr = XbdmBridge.DmOpenConnection(out _connHandle);
                        if (connHr != XbdmBridge.XBDM_NOERR && connHr != XbdmBridge.XBDM_CONNECTED)
                            errorMsg = $"DmOpenConnection failed: 0x{connHr:X8}";

                        if (XbdmBridge.DmGetSystemInfo(out var sysInfo) == XbdmBridge.XBDM_NOERR)
                        {
                            ushort maj = (ushort)(sysInfo.KernelVersion >> 16);
                            ushort min = (ushort)(sysInfo.KernelVersion & 0xFFFF);
                            kernel = $"{maj}.{min}";
                        }

                        XbdmBridge.DmGetConsoleType(out uint ct);
                        ctype = ct switch
                        {
                            1 => "Xenon", 2 => "Zephyr", 3 => "Falcon", 4 => "Jasper",
                            5 => "Trinity", 6 => "Corona", 7 => "Winchester",
                            _ => $"Unknown ({ct})"
                        };

                        ok = true;
                    }
                    else errorHr = hr;
                }
                catch (Exception ex) { errorMsg = ex.Message; }
            });

            if (ok)
            {
                ConsoleName = name;
                ConsoleIp = ip;
                KernelVersion = kernel;
                ConsoleType = ctype;
                ConnectionStatus = "Connected";
                ConnectionStatusColor = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 136));
                IsConnected = true;
                StatusText = $"Connected to {name} ({ip})";
                DriveItems.Clear();
                _ = LoadDrivesAsync();
            }
            else if (!string.IsNullOrEmpty(errorMsg))
            {
                ConnectionStatus = "Error";
                ConnectionStatusColor = System.Windows.Media.Brushes.Red;
                IsConnected = false;
                StatusText = $"Error: {errorMsg}";
            }
            else
            {
                ConnectionStatus = "Failed";
                ConnectionStatusColor = System.Windows.Media.Brushes.Red;
                IsConnected = false;
                StatusText = $"Could not connect. Error: 0x{errorHr:X8}";
            }

            IsBusy = false;
        }

        [RelayCommand]
        private async Task DisconnectAsync()
        {
            if (_connHandle != IntPtr.Zero)
            {
                await Task.Run(() => XbdmBridge.DmCloseConnection(_connHandle));
                _connHandle = IntPtr.Zero;
            }
            ConsoleName = "Not Connected";
            ConsoleIp = "—";
            KernelVersion = "—";
            ConsoleType = "—";
            CurrentTitle = "—";
            ConnectionStatus = "Disconnected";
            ConnectionStatusColor = System.Windows.Media.Brushes.Red;
            IsConnected = false;
            DriveItems.Clear();
            StatusText = "Disconnected.";
        }

        // ── Discovery / Scan ─────────────────────────────────────────────────
        [RelayCommand]
        private async Task ScanNetworkAsync()
        {
            IsScanning = true;
            IsBusy = true;
            StatusText = "Scanning for Xbox 360 consoles on network...";
            DiscoveredConsoles.Clear();

            string result = "";
            await Task.Run(() =>
            {
                try
                {
                    var consoles = XboxDiscovery.DiscoverAll();
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var c in consoles)
                            DiscoveredConsoles.Add(c);
                    });
                    result = consoles.Length > 0
                        ? $"Found {consoles.Length} console(s). Click one to connect."
                        : "No consoles found. Make sure your console is on and Xbox 360 Neighborhood is running.";
                }
                catch (Exception ex)
                {
                    result = $"Scan error: {ex.Message}";
                }
            });
            StatusText = result;

            IsScanning = false;
            IsBusy = false;
        }

        [RelayCommand]
        private async Task ConnectToDiscoveredAsync(DiscoveredConsole? console)
        {
            if (console == null) return;
            TargetConsole = console.Ip;
            await ConnectAsync();
        }

        // ── File Browser ────────────────────────────────────────────────────────
        private async Task LoadDrivesAsync()
        {
            string error = "";
            List<string> drives = new();
            await Task.Run(() =>
            {
                try
                {
                    var driveBuf = new byte[512];
                    uint driveSize = 512;
                    uint hr = XbdmBridge.DmGetDriveList(driveBuf, ref driveSize);
                    if (hr != XbdmBridge.XBDM_NOERR)
                    {
                        error = $"GetDriveList error: 0x{hr:X8}";
                        return;
                    }
                    int i = 0;
                    var sb = new StringBuilder();
                    while (i < driveSize)
                    {
                        if (driveBuf[i] == 0)
                        {
                            if (sb.Length > 0) { drives.Add(sb.ToString()); sb.Clear(); }
                            else break;
                        }
                        else sb.Append((char)driveBuf[i]);
                        i++;
                    }
                }
                catch (Exception ex) { error = ex.Message; }
            });
            if (!string.IsNullOrEmpty(error))
            {
                StatusText = $"File browser: {error}";
                return;
            }
            foreach (var d in drives)
            {
                var item = new Xbox360FileItem
                {
                    Name = d,
                    FullPath = $"{d}\\",
                    IsDirectory = true,
                    Icon = "💾",
                    Size = "Drive"
                };
                item.Children.Add(new Xbox360FileItem { Name = "Loading..." });
                DriveItems.Add(item);
            }
        }

        [RelayCommand]
        private async Task ExpandFolderAsync(Xbox360FileItem? item)
        {
            if (item == null || !item.IsDirectory) return;
            if (item.Children.Count == 1 && item.Children[0].Name == "Loading...")
            {
                item.Children.Clear();
                IsBusy = true;
                await Task.Run(() => LoadDirectory(item));
                IsBusy = false;
            }
        }

        private void LoadDirectory(Xbox360FileItem parent)
        {
            IntPtr handle = IntPtr.Zero;
            uint hr = XbdmBridge.DmOpenDir(parent.FullPath, out handle);
            if (hr != XbdmBridge.XBDM_NOERR || handle == IntPtr.Zero) return;

            var items = new List<Xbox360FileItem>();
            while (true)
            {
                hr = XbdmBridge.DmWalkDir(handle, out var fa);
                if (hr != XbdmBridge.XBDM_NOERR) break;

                bool isDir = (fa.Attributes & DmFileAttributes.Directory) != 0;
                long sz = ((long)fa.SizeHigh << 32) | fa.SizeLow;
                var child = new Xbox360FileItem
                {
                    Name = fa.Name,
                    FullPath = parent.FullPath.TrimEnd('\\') + "\\" + fa.Name,
                    IsDirectory = isDir,
                    Icon = isDir ? "📁" : GetFileIcon(fa.Name),
                    Size = isDir ? "" : FormatSize(sz)
                };
                if (isDir) child.Children.Add(new Xbox360FileItem { Name = "Loading..." });
                items.Add(child);
            }
            XbdmBridge.DmCloseDir(handle);

            App.Current.Dispatcher.Invoke(() =>
            {
                foreach (var it in items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name))
                    parent.Children.Add(it);
            });
        }

        [RelayCommand]
        private async Task DownloadFileAsync(Xbox360FileItem? item)
        {
            if (item == null || item.IsDirectory) return;
            IsBusy = true;
            StatusText = $"Downloading {item.Name}...";
            string result = "";
            await Task.Run(() =>
            {
                try
                {
                    string dest = Path.Combine(LocalSavePath, item.Name);
                    uint hr = XbdmBridge.DmReceiveFileA(dest, item.FullPath);
                    result = hr == XbdmBridge.XBDM_NOERR
                        ? $"Downloaded to {dest}"
                        : $"Download failed: 0x{hr:X8}";
                }
                catch (Exception ex) { result = $"Error: {ex.Message}"; }
            });
            StatusText = result;
            IsBusy = false;
        }

        [RelayCommand]
        private async Task UploadFileAsync(Xbox360FileItem? targetFolder)
        {
            if (targetFolder == null || !targetFolder.IsDirectory) return;

            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Select file to upload" };
            if (dlg.ShowDialog() != true) return;

            IsBusy = true;
            string remotePath = targetFolder.FullPath.TrimEnd('\\') + "\\" + Path.GetFileName(dlg.FileName);
            StatusText = $"Uploading {Path.GetFileName(dlg.FileName)}...";
            string result = "";
            await Task.Run(() =>
            {
                try
                {
                    uint hr = XbdmBridge.DmSendFileA(dlg.FileName, remotePath);
                    result = hr == XbdmBridge.XBDM_NOERR
                        ? "Upload complete."
                        : $"Upload failed: 0x{hr:X8}";
                }
                catch (Exception ex) { result = $"Error: {ex.Message}"; }
            });
            StatusText = result;
            IsBusy = false;
        }

        [RelayCommand]
        private async Task DeleteFileAsync(Xbox360FileItem? item)
        {
            if (item == null) return;
            IsBusy = true;
            StatusText = $"Deleting {item.Name}...";
            string result = "";
            await Task.Run(() =>
            {
                uint hr = XbdmBridge.DmDeleteFile(item.FullPath, item.IsDirectory);
                result = hr == XbdmBridge.XBDM_NOERR ? "Deleted." : $"Delete failed: 0x{hr:X8}";
            });
            StatusText = result;
            IsBusy = false;
        }

        // ── Console Control ─────────────────────────────────────────────────────
        [RelayCommand]
        private async Task RebootAsync()
        {
            IsBusy = true;
            StatusText = "Rebooting console...";
            await Task.Run(() => XbdmBridge.DmReboot(DmRebootFlags.Cold));
            StatusText = "Reboot command sent.";
            IsBusy = false;
        }

        [RelayCommand]
        private async Task RebootWarmAsync()
        {
            IsBusy = true;
            StatusText = "Warm rebooting...";
            await Task.Run(() => XbdmBridge.DmReboot(DmRebootFlags.Warm));
            StatusText = "Warm reboot command sent.";
            IsBusy = false;
        }

        [RelayCommand]
        private async Task TakeScreenshotAsync()
        {
            IsBusy = true;
            StatusText = "Capturing screenshot...";
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"xbox360_ss_{DateTime.Now:yyyyMMdd_HHmmss}.bmp");
            string result = "";
            string ssPath = "";
            await Task.Run(() =>
            {
                uint hr = XbdmBridge.DmScreenShot(path);
                result = hr == XbdmBridge.XBDM_NOERR
                    ? $"Screenshot saved to {path}"
                    : $"Screenshot failed: 0x{hr:X8}";
                if (hr == XbdmBridge.XBDM_NOERR) ssPath = path;
            });
            StatusText = result;
            if (!string.IsNullOrEmpty(ssPath)) ScreenshotPath = ssPath;
            IsBusy = false;
        }

        [RelayCommand]
        private async Task SendCommandAsync(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            IsBusy = true;
            await Task.Run(() =>
            {
                try
                {
                    var (hr, resp) = XbdmBridge.SendCommandRaw(command);
                    App.Current.Dispatcher.Invoke(() => StatusText = $"0x{hr:X8}: {resp}");
                }
                catch (Exception ex)
                {
                    App.Current.Dispatcher.Invoke(() => StatusText = $"EXCEPTION: {ex.Message}");
                }
            });
            IsBusy = false;
        }

        private static readonly Dictionary<string, string> TitleDb = new()
        {
            ["4D5307E6"] = "Minecraft: Xbox 360 Edition",
            ["4D53087D"] = "Minecraft (TU)",
            ["584108B7"] = "Call of Duty: Black Ops",
            ["584108C0"] = "Call of Duty: Black Ops II",
            ["58410954"] = "Call of Duty: Modern Warfare 3",
            ["5841120F"] = "Call of Duty: Ghosts",
            ["584113E3"] = "Call of Duty: Advanced Warfare",
            ["5841144F"] = "Call of Duty: Black Ops III",
            ["55530862"] = "Halo 3",
            ["555308B6"] = "Halo: Reach",
            ["4D53082B"] = "Halo: Combat Evolved Anniversary",
            ["4D5307ED"] = "Halo 4",
            ["454108DC"] = "Grand Theft Auto V",
            ["454109CF"] = "Grand Theft Auto IV",
            ["454108E6"] = "Red Dead Redemption",
            ["584109F4"] = "Skyrim",
            ["584109FF"] = "Fallout 3",
            ["58410B0F"] = "Fallout: New Vegas",
            ["555308C1"] = "Gears of War 3",
            ["555308A0"] = "Gears of War 2",
            ["58410A61"] = "The Elder Scrolls V: Skyrim",
            ["58410A53"] = "Forza Motorsport 4",
            ["58410B22"] = "Forza Horizon",
            ["58410BED"] = "Forza Horizon 2",
            ["58410962"] = "Mass Effect 2",
            ["58410A0D"] = "Mass Effect 3",
            ["4D5307FC"] = "BioShock Infinite",
            ["454108A8"] = "L.A. Noire",
            ["584109D0"] = "Dark Souls",
            ["58410A8F"] = "Dark Souls II",
            ["58410BA0"] = "Dark Souls III",
        };

        [RelayCommand]
        private async Task RefreshTitleAsync()
        {
            if (!IsConnected)
            {
                StatusText = "Refresh: not connected to console.";
                return;
            }

            StatusText = "Refreshing running title...";

            string titleId = "";
            string titleName = "";

            try
            {
                await Task.Run(() =>
                {
                    // Only the safe title-detection commands. `magicboot` is
                    // deliberately excluded: it forces a dev/XDK reboot of the
                    // console and is never the right answer to "what's running".
                    string[] cmds = { "xbeinfo running", "xbeinfo" };
                    foreach (var cmd in cmds)
                    {
                        var (hr, resp) = XbdmBridge.SendCommandRaw(cmd);
                        if (hr != XbdmBridge.XBDM_NOERR || string.IsNullOrEmpty(resp)) continue;

                        // Match `titleid=` or `title=` followed by 8 hex chars
                        // (with or without `0x` prefix). Anchored on a word
                        // boundary so we never false-match checksums,
                        // timestamps, or unrelated hex fields; the leading \b
                        // also prevents `subtitle=0x…` and similar substrings
                        // from being picked up as a title id.
                        var idMatch = System.Text.RegularExpressions.Regex.Match(
                            resp,
                            @"\b(?:titleid|title)\s*[=:]\s*(?:0x)?([0-9A-Fa-f]{8})",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (!idMatch.Success) continue;
                        if (idMatch.Groups[1].Value.All(c => c == '0')) continue;

                        titleId = idMatch.Groups[1].Value.ToUpperInvariant();

                        // Optional: pull `name="..."` for a friendlier caption
                        // when the title isn't in our local TitleDb.
                        var nameMatch = System.Text.RegularExpressions.Regex.Match(
                            resp,
                            @"name\s*=\s*""?([^""\r\n]+)""?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (nameMatch.Success)
                            titleName = nameMatch.Groups[1].Value.Trim();
                        break;
                    }
                });
            }
            catch (Exception ex)
            {
                StatusText = $"Refresh error: {ex.Message}";
                return;
            }

            _runningTitleId = titleId;

            if (!string.IsNullOrEmpty(titleId) && TitleDb.TryGetValue(titleId, out var known))
                CurrentTitle = known;
            else if (!string.IsNullOrEmpty(titleName))
                CurrentTitle = $"{titleName} (0x{titleId})";
            else if (!string.IsNullOrEmpty(titleId))
                CurrentTitle = $"Running (0x{titleId})";
            else
                CurrentTitle = "Dashboard (no game detected)";

            StatusText = !string.IsNullOrEmpty(titleId)
                ? $"Refresh: running title 0x{titleId}" + (string.IsNullOrEmpty(titleName) ? "" : $" — {titleName}")
                : "Refresh: no running title (dashboard).";
        }
        private string _runningTitleId = "";

        // ── Achievements ─────────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<AchievementItem> _achievements = new();
        [ObservableProperty] private bool _isAchievementBusy;
        [ObservableProperty] private string _achievementStatus = "";
        [ObservableProperty] private string _lastXbdmCommand = "";
        [ObservableProperty] private string _lastXbdmResponse = "";

        [RelayCommand]
        private async Task ScanAchievementsAsync()
        {
            if (!IsConnected || string.IsNullOrEmpty(_runningTitleId))
            { AchievementStatus = "No running title."; return; }
            IsAchievementBusy = true;
            Achievements.Clear();
            string raw = "";
            await Task.Run(() =>
            {
                LastXbdmCommand = $"xamachievement 0x{_runningTitleId}";
                var (hr, resp) = XbdmBridge.SendCommandRaw($"xamachievement 0x{_runningTitleId}");
                App.Current.Dispatcher.Invoke(() => LastXbdmResponse = resp);
                if (hr == XbdmBridge.XBDM_NOERR)
                    raw = resp;
                else
                    raw = $"hr=0x{hr:X8}";
            });
            if (raw.StartsWith("hr=") || raw.StartsWith("EXCEPTION"))
            { AchievementStatus = raw; IsAchievementBusy = false; return; }
            var parsed = ParseAchievementResponse(raw);
            foreach (var a in parsed) Achievements.Add(a);
            AchievementStatus = parsed.Count > 0
                ? $"Found {parsed.Count} achievement(s)"
                : $"Raw:\n{raw[..Math.Min(raw.Length, 600)]}";
            IsAchievementBusy = false;
        }

        private static List<AchievementItem> ParseAchievementResponse(string raw)
        {
            var list = new List<AchievementItem>();
            var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // detect delimiter: prefer tab, fall back to space
            int tabCount = 0, spaceCount = 0;
            foreach (var line in lines.Take(5))
            {
                if (line.Contains('\t')) tabCount++;
                else if (line.Contains(' ') && line.Count(c => c == ' ') >= 3) spaceCount++;
            }
            char sep = tabCount >= spaceCount ? '\t' : ' ';

            foreach (var line in lines)
            {
                var cols = line.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length < 4) continue;

                // First col could be id (number) or "achievement" / "title"
                if (!int.TryParse(cols[0].Trim(), out var id))
                {
                    // try col 1 or skip header lines
                    if (cols.Length > 1 && int.TryParse(cols[1].Trim(), out id)) { }
                    else continue;
                }

                var item = new AchievementItem
                {
                    Id = id,
                    Name = cols.Length > 1 ? cols[1].Trim().Trim('"') : "",
                    Description = cols.Length > 2 ? cols[2].Trim().Trim('"') : "",
                    Gamerscore = cols.Length > 3 && int.TryParse(cols[3].Trim(), out var gs) ? gs : 0,
                    IsUnlocked = cols.Length > 4 && (cols[4].Trim() == "1" || cols[4].Trim().Equals("unlocked", StringComparison.OrdinalIgnoreCase))
                };
                list.Add(item);
            }
            return list;
        }

        [RelayCommand]
        private async Task UnlockAchievementAsync(AchievementItem? item)
        {
            if (item == null || string.IsNullOrEmpty(_runningTitleId)) return;
            IsAchievementBusy = true;
            string msg = "";
            await Task.Run(() =>
            {
                LastXbdmCommand = $"xamachievement unlock 0x{_runningTitleId} {item.Id}";
                var (hr, resp) = XbdmBridge.SendCommandRaw($"xamachievement unlock 0x{_runningTitleId} {item.Id}");
                App.Current.Dispatcher.Invoke(() => LastXbdmResponse = resp);
                msg = hr == XbdmBridge.XBDM_NOERR ? resp : $"hr=0x{hr:X8}";
            });
            item.IsUnlocked = true;
            item.Status = "✓ Unlocked";
            AchievementStatus = $"Unlock: {msg}";
            IsAchievementBusy = false;
        }

        [RelayCommand]
        private async Task UnlockAllAchievementsAsync()
        {
            if (Achievements.Count == 0 || string.IsNullOrEmpty(_runningTitleId)) return;
            IsAchievementBusy = true;
            int ok = 0, fail = 0;
            foreach (var ach in Achievements)
            {
                if (ach.IsUnlocked) { ok++; continue; }
                await Task.Run(() =>
                {
                    LastXbdmCommand = $"xamachievement unlock 0x{_runningTitleId} {ach.Id}";
                    var (hr, _) = XbdmBridge.SendCommandRaw($"xamachievement unlock 0x{_runningTitleId} {ach.Id}");
                    if (hr == XbdmBridge.XBDM_NOERR) { ach.IsUnlocked = true; ach.Status = "✓ Unlocked"; ok++; }
                    else fail++;
                });
            }
            AchievementStatus = $"Done. {ok} unlocked, {fail} failed.";
            IsAchievementBusy = false;
        }

        // ── Offline Profile ─────────────────────────────────────────────────────
        [RelayCommand]
        private async Task OpenOfflineProfileAsync()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open Xbox 360 Profile (CON/PIRS)",
                Filter = "All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                OfflineProfilePath = dlg.FileName;
                OfflineProfileStatus = $"Loaded {Path.GetFileName(dlg.FileName)} (Experimental)";
                OfflineProfileGames.Clear();
                
                await Task.Run(() => 
                {
                    try
                    {
                        App.Current.Dispatcher.Invoke(() => {
                            OfflineProfileGames.Add("X360.dll experimental parsing active.");
                            OfflineProfileGames.Add("Full extraction/unlocking logic is pending DLL analysis.");
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Current.Dispatcher.Invoke(() => OfflineProfileStatus = "Error: " + ex.Message);
                    }
                });
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────
        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        }

        private static string GetFileIcon(string name)
        {
            string ext = Path.GetExtension(name).ToLower();
            return ext switch
            {
                ".xex" or ".exe" => "⚙️",
                ".xbe" => "🎮",
                ".stfs" or ".con" or ".pirs" => "📦",
                ".png" or ".jpg" or ".bmp" => "🖼️",
                ".mp3" or ".wav" or ".xma" => "🎵",
                ".mp4" or ".wmv" => "🎬",
                ".xml" or ".ini" or ".cfg" => "📝",
                ".gdf" or ".iso" => "💿",
                _ => "📄"
            };
        }
    }
}

