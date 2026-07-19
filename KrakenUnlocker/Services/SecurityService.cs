using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace KrakenUnlocker.Services;

/// <summary>
/// Machine fingerprinting, encrypted session tokens, anti-hooking, rate limiting.
/// </summary>
public static class SecurityService
{
    private static readonly byte[] _HmacKeyBytes = new byte[] { 0x11, 0x29, 0x1C, 0x36, 0x6D, 0x31, 0x3F, 0x39, 0x00, 0x53, 0x1C, 0x3A, 0x33, 0x09, 0x04, 0x59, 0x09, 0x00, 0x5F, 0x1F, 0x31, 0x5D, 0x40, 0x43, 0x46, 0x2C, 0x27, 0x46, 0x15, 0x05, 0x4B, 0x0D, 0x25, 0x33, 0x31, 0x3C, 0x3D, 0x20, 0xCB, 0xB2, 0xFB, 0xA2 };
    private static byte[]? _hmacKey;
    private static byte[] HmacKeyBytes => _hmacKey ??= _HmacKeyBytes;
    private static DateTime? _sessionTokenIssued;
    private static readonly object _rateLock = new();
    private static readonly Dictionary<string, List<DateTime>> _operationTimestamps = new();

    // ── MACHINE FINGERPRINT ──────────────────────────────────────────────────

    /// <summary>
    /// Generates a hardware fingerprint from multiple system identifiers.
    /// Returns a 32-char hex string unique to this machine.
    /// </summary>
    public static string GenerateFingerprint()
    {
        var parts = new List<string>();

        try
        {
            // CPU ID
            using var cpu = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
            foreach (ManagementObject obj in cpu.Get())
                parts.Add(obj["ProcessorId"]?.ToString() ?? "");
        }
        catch { }

        try
        {
            // Motherboard serial
            using var mb = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
            foreach (ManagementObject obj in mb.Get())
                parts.Add(obj["SerialNumber"]?.ToString() ?? "");
        }
        catch { }

        try
        {
            // BIOS serial
            using var bios = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
            foreach (ManagementObject obj in bios.Get())
                parts.Add(obj["SerialNumber"]?.ToString() ?? "");
        }
        catch { }

        try
        {
            // Disk serial
            using var disk = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive WHERE Index=0");
            foreach (ManagementObject obj in disk.Get())
                parts.Add(obj["SerialNumber"]?.ToString()?.Trim() ?? "");
        }
        catch { }

        try
        {
            // Windows Product ID
            using var os = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in os.Get())
                parts.Add(obj["SerialNumber"]?.ToString() ?? "");
        }
        catch { }

        try
        {
            // Machine GUID from registry
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = key?.GetValue("MachineGuid")?.ToString();
            if (!string.IsNullOrEmpty(guid)) parts.Add(guid);
        }
        catch { }

        // Primary MAC address
        try
        {
            var mac = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                     n.NetworkInterfaceType != NetworkInterfaceType.Loopback)?
                .GetPhysicalAddress().ToString();
            if (!string.IsNullOrEmpty(mac)) parts.Add(mac);
        }
        catch { }

        var raw = string.Join("|", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (string.IsNullOrEmpty(raw))
            raw = Environment.MachineName + Environment.ProcessorCount + Environment.OSVersion;

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLower();
    }

    // ── SESSION TOKEN (HMAC-SIGNED) ─────────────────────────────────────────

    /// <summary>
    /// Creates an HMAC-signed session token that proves this email+machine combo was verified.
    /// Format: base64(email_len:email:machineId:expiry_ticks:hmac)
    /// </summary>
    public static string CreateSessionToken(string email, string machineId, DateTime expiresAt)
    {
        var expiryTicks = expiresAt.Ticks;
        var payload = $"{email.Length}:{email}:{machineId}:{expiryTicks}";
        var hmac = ComputeHmac(payload);

        var tokenData = $"{payload}:{hmac}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(tokenData));
    }

    /// <summary>
    /// Validates an HMAC-signed session token. Returns (valid, email, machineId, expiresAt).
    /// </summary>
    public static (bool valid, string? email, string? machineId, DateTime? expiresAt) ValidateSessionToken(string token)
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length != 5) return (false, null, null, null);

            var emailLen = int.Parse(parts[0]);
            var email = parts[1];
            var machineId = parts[2];
            var expiryTicks = long.Parse(parts[3]);
            var receivedHmac = parts[4];

            if (email.Length != emailLen) return (false, null, null, null);

            var payload = $"{emailLen}:{email}:{machineId}:{expiryTicks}";
            var expectedHmac = ComputeHmac(payload);

            // Constant-time comparison to prevent timing attacks
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(receivedHmac),
                    Encoding.UTF8.GetBytes(expectedHmac)))
                return (false, null, null, null);

            var expiresAt = new DateTime(expiryTicks, DateTimeKind.Utc);

            // Check expiry
            if (expiresAt < DateTime.UtcNow)
                return (false, null, null, null);

            // Check machine ID matches
            var currentMachineId = LicenseService.GetMachineId();
            if (!string.Equals(machineId, currentMachineId, StringComparison.OrdinalIgnoreCase))
                return (false, null, null, null);

            return (true, email, machineId, expiresAt);
        }
        catch
        {
            return (false, null, null, null);
        }
    }

    private static string ComputeHmac(string data)
    {
        using var hmac = new HMACSHA256(HmacKeyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLower();
    }

    // ── ANTI-HOOKING ─────────────────────────────────────────────────────────

    /// <summary>
    /// Detects if critical Win32 API functions have been hooked (common bypass technique).
    /// </summary>
    public static bool DetectHooks()
    {
        try
        {
            // Check if ntdll's NtQueryInformationProcess has been hooked
            // by comparing the first bytes of the function with known clean values
            var ntdll = LoadLibraryW("ntdll.dll");
            if (ntdll != IntPtr.Zero)
            {
                var funcAddr = GetProcAddress(ntdll, "NtQueryInformationProcess");
                if (funcAddr != IntPtr.Zero)
                {
                    var firstBytes = new byte[8];
                    Marshal.Copy(funcAddr, firstBytes, 0, 8);

                    // Normal NtQueryInformationProcess starts with: 4C 8B D1 (mov r10, rcx)
                    // If the first byte is 0xE9 (jmp) or 0xFF (jmp rax), it's hooked
                    if (firstBytes[0] == 0xE9 || firstBytes[0] == 0xFF ||
                        firstBytes[0] == 0x48 && firstBytes[1] == 0xFF) // jmp qword [addr]
                        return true;
                }

                var funcAddr2 = GetProcAddress(ntdll, "NtSetInformationThread");
                if (funcAddr2 != IntPtr.Zero)
                {
                    var firstBytes = new byte[8];
                    Marshal.Copy(funcAddr2, firstBytes, 0, 8);
                    if (firstBytes[0] == 0xE9 || firstBytes[0] == 0xFF)
                        return true;
                }

                var funcAddr3 = GetProcAddress(ntdll, "NtSetInformationProcess");
                if (funcAddr3 != IntPtr.Zero)
                {
                    var firstBytes = new byte[8];
                    Marshal.Copy(funcAddr3, firstBytes, 0, 8);
                    if (firstBytes[0] == 0xE9 || firstBytes[0] == 0xFF)
                        return true;
                }

                FreeLibrary(ntdll);
            }

            // Check kernel32 hooks
            var kernel32 = LoadLibraryW("kernel32.dll");
            if (kernel32 != IntPtr.Zero)
            {
                var funcs = new[] { "IsDebuggerPresent", "CheckRemoteDebuggerPresent", "GetCurrentProcess" };
                foreach (var func in funcs)
                {
                    var addr = GetProcAddress(kernel32, func);
                    if (addr != IntPtr.Zero)
                    {
                        var firstBytes = new byte[8];
                        Marshal.Copy(addr, firstBytes, 0, 8);
                        if (firstBytes[0] == 0xE9 || firstBytes[0] == 0xFF)
                            return true;
                    }
                }
                FreeLibrary(kernel32);
            }

            // Check if a debugger is attached via the CLR debugger check
            if (Debugger.IsAttached) return true;
            if (Debugger.IsLogging()) return true;
        }
        catch { }

        return false;
    }

    // ── CLIENT-SIDE RATE LIMITING ────────────────────────────────────────────

    /// <summary>
    /// Checks if an operation is allowed under rate limiting.
    /// Returns true if allowed, false if rate limited.
    /// </summary>
    public static bool CheckRateLimit(string operationType, int maxPerMinute = 10)
    {
        lock (_rateLock)
        {
            var now = DateTime.UtcNow;
            if (!_operationTimestamps.ContainsKey(operationType))
                _operationTimestamps[operationType] = new List<DateTime>();

            // Remove entries older than 1 minute
            _operationTimestamps[operationType].RemoveAll(t => (now - t).TotalSeconds > 60);

            if (_operationTimestamps[operationType].Count >= maxPerMinute)
                return false;

            _operationTimestamps[operationType].Add(now);
            return true;
        }
    }

    // ── SESSION VALIDATION ───────────────────────────────────────────────────

    /// <summary>
    /// Validates the current session is still authentic.
    /// Called periodically by the watchdog.
    /// </summary>
    public static bool ValidateSession()
    {
        try
        {
            if (!LicenseService.IsPremium) return true; // not logged in, nothing to validate

            // Check if session token exists and is valid
            var token = GetStoredToken();
            if (string.IsNullOrEmpty(token)) return false;

            var (valid, _, _, _) = ValidateSessionToken(token);
            return valid;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stores the session token in an obfuscated location.
    /// </summary>
    public static void StoreToken(string token)
    {
        try
        {
            var path = GetTokenPath();
            // XOR-encode the token for light obfuscation (not real encryption, just prevents casual reading)
            var encoded = XorEncode(token, GetCurrentXorKey());
            File.WriteAllText(path, encoded);
            _sessionTokenIssued = DateTime.UtcNow;
        }
        catch { }
    }

    /// <summary>
    /// Retrieves and validates the stored session token.
    /// </summary>
    public static string? GetStoredToken()
    {
        try
        {
            var path = GetTokenPath();
            if (!File.Exists(path)) return null;
            var encoded = File.ReadAllText(path);
            return XorEncode(encoded, GetCurrentXorKey());
        }
        catch { return null; }
    }

    /// <summary>
    /// Clears the stored session token.
    /// </summary>
    public static void ClearStoredToken()
    {
        try
        {
            var path = GetTokenPath();
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static string GetTokenPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KrakenXboxUnlocker");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, ".session");
    }

    private static string GetCurrentXorKey()
    {
        // Key changes daily so stale tokens become invalid
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        return "KxU_" + today + "_3nd";
    }

    private static string XorEncode(string input, string key)
    {
        var result = new char[input.Length];
        for (int i = 0; i < input.Length; i++)
            result[i] = (char)(input[i] ^ key[i % key.Length]);
        return new string(result);
    }

    // ── SERVER-SIDE PER-OPERATION VALIDATION ─────────────────────────────────
    private static readonly HttpClient _opHttp = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Dictionary<string, (string token, DateTime expiry)> _tokenCache = new();
    private static readonly object _tokenLock = new();

    /// <summary>
    /// Validates a premium operation with the server and caches the result.
    /// Must be called before ANY premium write operation (unlock, spoof, stat-edit).
    /// Returns true if the operation is allowed.
    /// </summary>
    public static async Task<bool> ValidatePremiumOpAsync(string opType, string titleId = "")
    {
        if (!LicenseService.IsPremium || string.IsNullOrEmpty(LicenseService.CurrentEmail))
            return false;

        var cacheKey = $"{opType}:{titleId}";
        lock (_tokenLock)
        {
            if (_tokenCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow < cached.expiry)
                return true;
        }

        try
        {
            var email = LicenseService.CurrentEmail;
            var machineId = LicenseService.GetMachineId();
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var url = $"{Secrets.SupabaseUrl}/functions/v1/kraken-auth?action=validate-unlock" +
                      $"&email={Uri.EscapeDataString(email)}" +
                      $"&machine_id={Uri.EscapeDataString(machineId)}" +
                      $"&op={Uri.EscapeDataString(opType)}" +
                      $"&title_id={Uri.EscapeDataString(titleId)}" +
                      $"&ts={timestamp}";

            _opHttp.DefaultRequestHeaders.Clear();
            _opHttp.DefaultRequestHeaders.Add("apikey", Secrets.SupabaseAnonKey);

            var resp = await _opHttp.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            var json = Newtonsoft.Json.Linq.JObject.Parse(body);

            if (json["valid"]?.ToObject<bool>() == true)
            {
                var expires = json["expires"]?.ToObject<long>() ?? 0;
                var expiry = DateTimeOffset.FromUnixTimeMilliseconds(expires).UtcDateTime;
                var token = json["token"]?.ToString() ?? "";

                lock (_tokenLock)
                    _tokenCache[cacheKey] = (token, expiry);

                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);
}
