using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace KrakenUnlocker.Services
{
    /// <summary>
    /// Checks the running DLL hasn't been tampered with by comparing its SHA256 hash
    /// against a known-good hash published to GitHub releases.
    /// We hash the DLL (not the tiny host exe) because MasonProtector protects the DLL.
    /// </summary>
    public static class IntegrityService
    {
        private static readonly byte[] _GitHubLatestUrl = new byte[] { 0x70, 0x53, 0x01, 0x1D, 0xB4, 0x59, 0x5A, 0x67, 0xAC, 0xC8, 0x7F, 0x96, 0xC3, 0x0D, 0x83, 0x9E, 0x49, 0xF3, 0xFC, 0x8F, 0xBB, 0x29, 0xB9, 0x86, 0x7D, 0x85, 0x6A, 0x03, 0xC3, 0xEB, 0x32, 0x17, 0xFE, 0x2C, 0xC5, 0x06, 0x4F, 0x71, 0xC7, 0xF9, 0x93, 0xED, 0x6E, 0x28, 0xC7, 0xDC, 0xC9, 0x49, 0x2D, 0x88, 0x10, 0xD0, 0x92, 0x25, 0x15, 0x89, 0xD0, 0xFE, 0x64, 0x3D, 0xE7, 0x9B, 0x1B, 0x34, 0x73, 0x04, 0x8B, 0x0E, 0x91, 0xF7, 0x1D, 0xF9, 0x1C, 0x16, 0x88, 0x1F, 0x88, 0x25, 0xEB, 0x1D, 0x84, 0x7B, 0xC8, 0x5C, 0xBE, 0x62, 0x01, 0x04, 0x39, 0xA5, 0xD6, 0xDA, 0xF0, 0xEE, 0xEB, 0x6A };
        private static readonly byte[] _HashFileName = new byte[] { 0xC5, 0x9C, 0xF5, 0x42, 0x14, 0x3F, 0x64, 0x6D, 0x0E, 0x32, 0xAF, 0xA8, 0x45, 0x17, 0x17, 0x19, 0x1E, 0x38, 0x8B, 0x98, 0x69, 0x92, 0x4D, 0xA2, 0x1F, 0x84, 0x09, 0x9F, 0x57, 0x94, 0x01, 0x47, 0x36, 0xE0, 0xEE, 0x2B, 0x5E, 0x4A, 0x21, 0x7E, 0xAA, 0x5C, 0x8D, 0x39, 0xE7, 0x2B, 0x0B, 0xA8 };

        private static string? _releasesUrl;
        private static string? _hashFileName;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public enum IntegrityResult
        {
            Ok,           // Hash matches — DLL is clean
            Tampered,     // Hash mismatch — DLL has been modified
            Unverifiable  // Could not fetch hash (offline, file missing) — allow but warn
        }

        public static async Task<IntegrityResult> CheckAsync()
        {
            try
            {
                // Hash the exe (single-file bundles everything into one .exe)
                var exeDir = AppContext.BaseDirectory;
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) 
                    exePath = Path.Combine(exeDir, "KrakenXboxUnlocker.exe");
                if (!File.Exists(exePath)) return IntegrityResult.Unverifiable;

                using var stream = File.OpenRead(exePath);
                var localHash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLower();

                // Fetch the known-good hash from latest GitHub release asset
                Http.DefaultRequestHeaders.Clear();
                Http.DefaultRequestHeaders.Add("User-Agent", "KrakenXboxUnlocker");

                var releasesUrl = _releasesUrl ??= StringCryptor.Decode(_GitHubLatestUrl);
                var hashFileName = _hashFileName ??= StringCryptor.Decode(_HashFileName);
                var json = await Http.GetStringAsync(releasesUrl);
                var release = System.Text.Json.JsonDocument.Parse(json);

                string? hashFileUrl = null;
                foreach (var asset in release.RootElement.GetProperty("assets").EnumerateArray())
                {
                    if (asset.GetProperty("name").GetString() == hashFileName)
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
        /// Compute and save the SHA256 hash of a DLL to a file.
        /// Run this after building a release to generate the hash file to upload to GitHub.
        /// </summary>
        public static async Task GenerateHashFileAsync(string dllPath, string outputPath)
        {
            using var stream = File.OpenRead(dllPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLower();
            await File.WriteAllTextAsync(outputPath, hash);
        }
    }
}
