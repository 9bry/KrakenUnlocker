using System.Net.Http;
using Newtonsoft.Json;

namespace KrakenUnlocker.Services
{
    public static class UpdateService
    {
        public const string CurrentVersion = "1.0";

        public enum UpdateSeverity
        {
            None,       // Up to date
            Soft,       // One version behind — premium locked, free works
            Hard        // Two or more versions behind — all features blocked
        }

        public class UpdateCheckResult
        {
            public UpdateSeverity Severity { get; init; } = UpdateSeverity.None;
            public GitHubRelease? LatestRelease { get; init; }
            public int VersionsBehind { get; init; }
        }

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        /// <summary>
        /// Checks GitHub for updates and returns severity based on how far behind the user is.
        /// Soft = 1 version behind, Hard = 2+ versions behind.
        /// </summary>
        public static async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            try
            {
                Http.DefaultRequestHeaders.Clear();
                Http.DefaultRequestHeaders.Add("User-Agent", "KrakenXboxUnlocker");

                // Get ALL releases to determine how many versions behind
                var allUrl = $"https://api.github.com/repos/9bry/KrakenUnlocker/releases?per_page=20";
                var json = await Http.GetStringAsync(allUrl);
                var releases = JsonConvert.DeserializeObject<List<GitHubRelease>>(json);

                if (releases == null || releases.Count == 0)
                    return new UpdateCheckResult { Severity = UpdateSeverity.None };

                var current = new Version(CurrentVersion.TrimStart('v').Trim());

                // Only the newest published release matters for an update check.
                // Comparing against every historical tag caused false "v3" popups
                // (e.g. when an old build counted tags that no longer exist).
                GitHubRelease? latest = null;
                foreach (var r in releases.OrderByDescending(r => r.TagName))
                {
                    if (r.TagName == null) continue;
                    if (!Version.TryParse(r.TagName.TrimStart('v').Trim(), out _)) continue;
                    latest = r;
                    break;
                }

                if (latest == null)
                    return new UpdateCheckResult { Severity = UpdateSeverity.None };

                var remoteVer = new Version(latest.TagName!.TrimStart('v').Trim());
                if (remoteVer <= current)
                    return new UpdateCheckResult { Severity = UpdateSeverity.None };

                return new UpdateCheckResult
                {
                    Severity = UpdateSeverity.Hard,
                    LatestRelease = latest,
                    VersionsBehind = 1
                };
            }
            catch
            {
                // Force a Hard Block if the check fails (e.g. offline, deleted repo)
                return new UpdateCheckResult 
                { 
                    Severity = UpdateSeverity.Hard,
                    LatestRelease = new GitHubRelease
                    {
                        TagName = "Error",
                        Body = "Unable to connect to the update server to verify your version.\n\nFor security reasons, the app is locked until a connection can be established or you manually download the latest version.",
                        HtmlUrl = "https://github.com/9bry/KrakenUnlocker/releases/latest"
                    }
                };
            }
        }

        // Legacy method kept for compatibility
        public static async Task<GitHubRelease?> GetPendingUpdateAsync()
        {
            var result = await CheckForUpdateAsync();
            return result.LatestRelease;
        }
    }
}