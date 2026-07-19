using System.Net.Http;
using Newtonsoft.Json;

namespace KrakenUnlocker.Services
{
    public static class UpdateService
    {
        public const string CurrentVersion = "1.2";

        private static readonly byte[] _GitHubAllReleases = new byte[] { 0x7C, 0xA8, 0x4C, 0x5D, 0xFA, 0xDD, 0xF1, 0xB5, 0xE9, 0x96, 0x9B, 0xFA, 0x8F, 0x2E, 0x44, 0x28, 0x53, 0x50, 0xF1, 0x06, 0xF7, 0x78, 0x93, 0xA6, 0xE6, 0xB1, 0xCC, 0x09, 0x25, 0xD6, 0x54, 0x1F, 0xDC, 0x15, 0x31, 0x2A, 0x7B, 0x35, 0x67, 0x55, 0x36, 0x1C, 0x30, 0x01, 0x8E, 0x0A, 0x71, 0xA2, 0x53, 0xFC, 0xBB, 0xC7, 0xFE, 0xD0, 0xFC, 0x33, 0x0A, 0x5E, 0x67, 0xE5, 0x26, 0x60, 0xF2, 0x7D, 0x3E, 0x75, 0x41, 0x72, 0xEC, 0x39, 0x72, 0x98, 0xCC, 0x8C, 0xCF, 0x41, 0x6E, 0xE0, 0xBD, 0xCA, 0x61, 0x57, 0x8F, 0x82, 0xEF, 0x23, 0x33, 0xCD, 0x55, 0x7A, 0xD5, 0x7C, 0x08, 0xAA, 0x7D, 0xBA };
        private static readonly byte[] _GHReleasePage = new byte[] { 0x41, 0xE5, 0x91, 0x4D, 0xAB, 0x82, 0x0B, 0x83, 0xD5, 0xB7, 0x86, 0x10, 0xB2, 0x21, 0x49, 0xC4, 0x7A, 0xAD, 0xA6, 0xFA, 0x8C, 0x91, 0x55, 0xED, 0x01, 0x98, 0x30, 0xAF, 0x0C, 0xAE, 0xE0, 0x14, 0xBB, 0x94, 0x3D, 0x38, 0x0A, 0x57, 0x46, 0x00, 0xB8, 0x3A, 0x72, 0x79, 0xF6, 0x13, 0x25, 0xA4, 0x01, 0x51, 0xDA, 0x9B, 0x1A, 0xAF, 0xDE, 0x52, 0xA8, 0x23, 0xF4, 0x80, 0xFE, 0x9D, 0xDC, 0xC0, 0xA3, 0x05, 0x3F, 0xF0, 0x5D, 0x2D, 0xD6, 0xAD, 0x47, 0x79, 0x9B, 0x0E, 0xF9, 0x50, 0xB5, 0x1F };
        private static string? _allReleasesUrl;
        private static string? _releasePageUrl;

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
                var allUrl = _allReleasesUrl ??= StringCryptor.Decode(_GitHubAllReleases);
                var json = await Http.GetStringAsync(allUrl);
                var releases = JsonConvert.DeserializeObject<List<GitHubRelease>>(json);

                if (releases == null || releases.Count == 0)
                    return new UpdateCheckResult { Severity = UpdateSeverity.None };

                var current = new Version(CurrentVersion.TrimStart('v').Trim());

                // Filter to valid, non-draft, non-prerelease tags and sort by Version
                GitHubRelease? latest = null;
                var validReleases = new List<(GitHubRelease release, Version version)>();
                foreach (var r in releases)
                {
                    if (r.Draft || r.Prerelease) continue;
                    if (r.TagName == null) continue;
                    if (!Version.TryParse(r.TagName.TrimStart('v').Trim(), out var parsed)) continue;
                    validReleases.Add((r, parsed));
                }

                if (validReleases.Count == 0)
                    return new UpdateCheckResult { Severity = UpdateSeverity.None };

                latest = validReleases.OrderByDescending(x => x.version).First().release;
                var remoteVer = validReleases.OrderByDescending(x => x.version).First().version;

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
                        HtmlUrl = _releasePageUrl ??= StringCryptor.Decode(_GHReleasePage)
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