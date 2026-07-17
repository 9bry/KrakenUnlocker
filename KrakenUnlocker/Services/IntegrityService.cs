using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace KrakenUnlocker.Services
{
    /// <summary>
    /// Checks the running EXE hasn't been tampered with by comparing its SHA256 hash
    /// against a known-good hash published to GitHub releases.
    /// </summary>
    public static class IntegrityService
    {
        private const string RepoOwner = "9bry";
        private const string RepoName  = "KrakenUnlocker";

        // Name of the hash file published alongside the EXE in each GitHub release
        private const string HashFileName = "KrakenXboxUnlocker.sha256";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public enum IntegrityResult
        {
            Ok,           // Hash matches — EXE is clean
            Tampered,     // Hash mismatch — EXE has been modified
            Unverifiable  // Could not fetch hash (offline, file missing) — allow but warn
        }

        public static async Task<IntegrityResult> CheckAsync()
        {
            try
            {
                // Get the hash of the currently running EXE
                var exePath = Environment.ProcessPath;
                if (exePath == null) return IntegrityResult.Unverifiable;

                using var stream = File.OpenRead(exePath);
                var localHash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLower();

                // Fetch the known-good hash from latest GitHub release asset
                Http.DefaultRequestHeaders.Clear();
                Http.DefaultRequestHeaders.Add("User-Agent", "KrakenXboxUnlocker");

                var releasesUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var json = await Http.GetStringAsync(releasesUrl);
                var release = System.Text.Json.JsonDocument.Parse(json);

                string? hashFileUrl = null;
                foreach (var asset in release.RootElement.GetProperty("assets").EnumerateArray())
                {
                    if (asset.GetProperty("name").GetString() == HashFileName)
                    {
                        hashFileUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                if (hashFileUrl == null) return IntegrityResult.Unverifiable;

                var remoteHash = (await Http.GetStringAsync(hashFileUrl)).Trim().ToLower();

                return string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase)
                    ? IntegrityResult.Ok
                    : IntegrityResult.Tampered;
            }
            catch
            {
                return IntegrityResult.Unverifiable;
            }
        }

        /// <summary>
        /// Compute and save the SHA256 hash of the current EXE to a file.
        /// Run this after building a release to generate the hash file to upload to GitHub.
        /// </summary>
        public static async Task GenerateHashFileAsync(string exePath, string outputPath)
        {
            using var stream = File.OpenRead(exePath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLower();
            await File.WriteAllTextAsync(outputPath, hash);
        }
    }
}
