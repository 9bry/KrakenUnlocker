using System.Diagnostics;
using KrakenUnlocker.Services;

namespace KrakenUnlocker.Views.Windows
{
    public partial class UpdateWindow : System.Windows.Window
    {
        private readonly string _downloadUrl;
        private readonly bool _hardBlock;

        public UpdateWindow(GitHubRelease release, bool hardBlock = false)
        {
            InitializeComponent();

            _hardBlock = hardBlock;

            CurrentVersionText.Text = $"v{UpdateService.CurrentVersion}";
            NewVersionText.Text      = release.TagName ?? "Unknown";

            ChangelogText.Text = string.IsNullOrWhiteSpace(release.Body)
                ? "No changelog provided."
                : release.Body.Trim();

            _downloadUrl = release.Assets?.FirstOrDefault(a => a.BrowserDownloadUrl?.EndsWith(".zip") == true)?.BrowserDownloadUrl
                           ?? release.Assets?.FirstOrDefault(a => a.BrowserDownloadUrl?.EndsWith(".exe") == true)?.BrowserDownloadUrl
                           ?? release.HtmlUrl
                           ?? $"https://github.com/9bry/KrakenUnlocker/releases/latest";

            // Hard block logic handles OnClosing
            if (hardBlock)
            {
                Loaded += (_, _) =>
                {
                    if (FindName("WarningText") is System.Windows.Controls.TextBlock tb)
                        tb.Text = "The app is locked until you update.";
                };
            }
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void DownloadButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true });
        }


        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Prevent closing the window — user MUST download
            e.Cancel = true;
            base.OnClosing(e);
        }
    }
}
