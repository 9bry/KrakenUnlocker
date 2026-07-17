using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Security.Cryptography;
using System.Management;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KrakenUnlocker.Services
{
    public static class LicenseService
    {
        private static readonly string EdgeUrl = Secrets.SupabaseUrl + "/functions/v1/kraken-auth";
        private static readonly string AnonKey = Secrets.SupabaseAnonKey;
        private static readonly HttpClient Http = new();
        public static string? LastRestoreError { get; private set; }

        public static event Action? StateChanged;

        private static string? _currentEmail;
        public static string? CurrentEmail
        {
            get => _currentEmail;
            private set { _currentEmail = value; StateChanged?.Invoke(); }
        }

        private static bool _isPremium;
        public static bool IsPremium
        {
            get => _isPremium;
            private set { _isPremium = value; StateChanged?.Invoke(); }
        }

        private static DateTime? _expiresAt;
        public static DateTime? ExpiresAt
        {
            get => _expiresAt;
            set { _expiresAt = value; StateChanged?.Invoke(); }
        }

        private static bool _isLifetime;
        public static bool IsLifetime
        {
            get => _isLifetime;
            private set { _isLifetime = value; StateChanged?.Invoke(); }
        }

        private static int _daysLeft;
        public static int DaysLeft
        {
            get => _daysLeft;
            private set { _daysLeft = value; StateChanged?.Invoke(); }
        }

        private static string _expiryDisplay = "";
        public static string ExpiryDisplay
        {
            get => _expiryDisplay;
            private set { _expiryDisplay = value; StateChanged?.Invoke(); }
        }

        static LicenseService()
        {
            Http.DefaultRequestHeaders.Add("apikey", AnonKey);
            Http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", AnonKey);
        }

        private static string? _cachedMachineId;
        public static string GetMachineId()
        {
            if (_cachedMachineId != null) return _cachedMachineId;
            try
            {
                var parts = new List<string>();
                using var cpuSearcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (ManagementObject obj in cpuSearcher.Get())
                    parts.Add(obj["ProcessorId"]?.ToString() ?? "");
                using var mbSearcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in mbSearcher.Get())
                    parts.Add(obj["SerialNumber"]?.ToString() ?? "");
                var raw = string.Join("|", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
                if (string.IsNullOrEmpty(raw)) raw = Environment.MachineName;
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
                _cachedMachineId = Convert.ToHexString(hash)[..16].ToLower();
            }
            catch
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName));
                _cachedMachineId = Convert.ToHexString(hash)[..16].ToLower();
            }
            return _cachedMachineId;
        }

        public static async Task<(bool success, string error)> SendCodeAsync(string email)
        {
            try
            {
                var resp = await Http.PostAsync(
                    $"{EdgeUrl}?action=send-code&email={Uri.EscapeDataString(email)}",
                    null);
                var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                if (!resp.IsSuccessStatusCode)
                    return (false, json["error"]?.ToString() ?? "Failed to send code.");
                if (json["sent"]?.ToObject<bool>() != true)
                    return (false, json["error"]?.ToString() ?? "Failed to send code.");
                return (true, "");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public static async Task<(bool success, string error)> VerifyCodeAsync(string email, string code)
        {
            try
            {
                var machineId = GetMachineId();
                var resp = await Http.PostAsync(
                    $"{EdgeUrl}?action=verify-code&email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(code)}&machine_id={machineId}",
                    null);
                var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                if (!resp.IsSuccessStatusCode)
                    return (false, json["error"]?.ToString() ?? "Invalid or expired code.");
                var expiresAtStr = json["expires_at"]?.ToString();
                SetLicenseDetails(email, expiresAtStr);
                return (true, "");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        private static void SetLicenseDetails(string email, string? expiresAtStr)
        {
            CurrentEmail = email;
            IsPremium    = true;
            ExpiresAt    = DateTime.Now.AddHours(15);

            if (string.IsNullOrEmpty(expiresAtStr) || expiresAtStr == "null")
            {
                IsLifetime   = true;
                DaysLeft     = 99999;
                ExpiryDisplay = "♾ Lifetime";
            }
            else
            {
                IsLifetime = false;
                var licenseExpiry = DateTime.Parse(expiresAtStr, null,
                    System.Globalization.DateTimeStyles.RoundtripKind);
                DaysLeft = Math.Max(0, (int)(licenseExpiry - DateTime.UtcNow).TotalDays);
                ExpiryDisplay = DaysLeft == 0 ? "Expires today" : $"{DaysLeft} days remaining";
            }
        }

        public static void Logout()
        {
            CurrentEmail = null;
            IsPremium    = false;
            ExpiresAt    = null;
        }
    }
}
