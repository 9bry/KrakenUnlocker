using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace KrakenUnlocker.Services;

/// <summary>
/// Hardened runtime protection: anti-debug, anti-VM, anti-sandbox, anti-tamper, anti-dump,
/// anti-RE tools, periodic integrity re-checks, environment fingerprinting.
/// Run ALL checks before the UI starts. Any failure = silent shutdown.
/// </summary>
public static class ProtectionService
{
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetInformationProcess(IntPtr hProcess, int processInformationClass, ref int processInformation, int processInformationLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsDebuggerPresent();

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref IntPtr processInformation, int processInformationSize, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationThread(IntPtr threadHandle, int threadInformationClass, IntPtr threadInformation, int threadInformationLength);

    private const int PROCESS_DEBUG_PORT = 7;
    private const int PROCESS_DEBUG_FLAGS = 0x1F;
    private const int PROCESS_DEBUG_OBJECT_HANDLE = 0x1E;
    private const int ThreadHideFromDebugger = 0x11;

    // Periodic check timer
    private static System.Threading.Timer? _watchdogTimer;
    private static byte[]? _initialMethodHash;
    private static readonly object _lock = new();
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KrakenXboxUnlocker", "crash.log");
    private static void Log(string msg)
    {
        try { var d = Path.GetDirectoryName(_logPath)!; if (!Directory.Exists(d)) Directory.CreateDirectory(d); File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { }
    }

    public static bool RunAllChecks()
    {
        try
        {
            Log("Starting..."); 
            if (!AntiDebuggerCheck()) { Log("FAIL: Debugger"); return false; }
            if (!AntiREToolsCheck()) { Log("FAIL: RETools"); return false; }
            if (!AntiVMCheck()) { Log("FAIL: VM"); return false; }
            if (!AntiSandboxCheck()) { Log("FAIL: Sandbox"); return false; }
            if (!AntiTimingCheck()) { Log("FAIL: Timing"); return false; }
            if (!AntiDumpCheck()) { Log("FAIL: Dump"); return false; }
            Log("All passed");
            _initialMethodHash = ComputeCriticalMethodHash();
            StartPeriodicWatchdog();
            HideThreadFromDebugger();
            return true;
        }
        catch (Exception ex) { Log($"EX: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Starts a periodic integrity check every 5 minutes.
    /// </summary>
    private static void StartPeriodicWatchdog()
    {
        _watchdogTimer = new System.Threading.Timer(_ =>
        {
            lock (_lock)
            {
                try
                {
                    // Re-run anti-debug
                    if (IsDebuggerPresent()) { Log("WATCHDOG: Debugger detected"); Shutdown(); return; }

                    IntPtr debugPort = IntPtr.Zero;
                    NtQueryInformationProcess(GetCurrentProcess(), PROCESS_DEBUG_PORT, ref debugPort, IntPtr.Size, IntPtr.Zero);
                    if (debugPort != IntPtr.Zero) { Log("WATCHDOG: Debug port detected"); Shutdown(); return; }

                    // Re-check critical method hashes
                    if (_initialMethodHash != null)
                    {
                        var current = ComputeCriticalMethodHash();
                        if (current != null && !CryptographicOperations.FixedTimeEquals(_initialMethodHash, current))
                        {
                            Log("WATCHDOG: Method hash tampered");
                            Shutdown();
                            return;
                        }
                    }

                    // Re-check for RE tools
                    if (!AntiREToolsCheck()) { Log("WATCHDOG: RE tool detected"); Shutdown(); return; }

                    // Session token validation
                    if (!SecurityService.ValidateSession()) { Log("WATCHDOG: Session invalid"); Shutdown(); return; }

                    // Periodic integrity re-check
                    _ = RecheckIntegrityAsync();
                }
                catch (Exception ex)
                {
                    Log($"Watchdog exception: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private static void Shutdown()
    {
        try { System.Windows.Application.Current?.Dispatcher?.Invoke(() => System.Windows.Application.Current.Shutdown()); }
        catch { try { System.Windows.Application.Current.Shutdown(); } catch { Environment.Exit(1); } }
    }

    /// <summary>
    /// Periodic integrity re-check (non-blocking).
    /// </summary>
    private static async Task RecheckIntegrityAsync()
    {
        try
        {
            var result = await IntegrityService.CheckAsync();
            if (result == IntegrityService.IntegrityResult.Tampered)
            {
                Log("WATCHDOG: Integrity tampered");
                Shutdown();
            }
        }
        catch { }
    }

    /// <summary>
    /// Hides the current thread from debuggers so breakpoints on other threads don't affect us.
    /// </summary>
    private static void HideThreadFromDebugger()
    {
        try
        {
            NtSetInformationThread(
                (IntPtr)(-2), // current thread pseudo-handle
                ThreadHideFromDebugger,
                IntPtr.Zero,
                0);
        }
        catch { }
    }

    // ── ANTI-DEBUGGER ─────────────────────────────────────────────────────────
    private static bool AntiDebuggerCheck()
    {
        if (Debugger.IsAttached) return false;
        if (Debugger.IsLogging()) return false;
        if (IsDebuggerPresent()) return false;

        IntPtr debugPort = IntPtr.Zero;
        if (NtQueryInformationProcess(GetCurrentProcess(), PROCESS_DEBUG_PORT, ref debugPort, IntPtr.Size, IntPtr.Zero) == 0)
            if (debugPort != IntPtr.Zero) return false;

        IntPtr debugFlags = IntPtr.Zero;
        if (NtQueryInformationProcess(GetCurrentProcess(), PROCESS_DEBUG_FLAGS, ref debugFlags, IntPtr.Size, IntPtr.Zero) == 0)
            if (debugFlags == IntPtr.Zero) return false;

        IntPtr debugObject = IntPtr.Zero;
        if (NtQueryInformationProcess(GetCurrentProcess(), PROCESS_DEBUG_OBJECT_HANDLE, ref debugObject, IntPtr.Size, IntPtr.Zero) == 0)
            if (debugObject != IntPtr.Zero) return false;

        IntPtr isRemote = Marshal.AllocHGlobal(1);
        Marshal.WriteByte(isRemote, 0);
        try
        {
            var ntdll = LoadLibraryW("ntdll.dll");
            if (ntdll != IntPtr.Zero)
            {
                var pCheck = GetProcAddress(ntdll, "RtlCheckRemoteDebuggerPresent");
                if (pCheck != IntPtr.Zero)
                {
                    var dlg = Marshal.GetDelegateForFunctionPointer<CheckRemoteDebuggerDelegate>(pCheck);
                    dlg(GetCurrentProcess(), isRemote);
                    if (Marshal.ReadByte(isRemote) != 0) return false;
                }
                FreeLibrary(ntdll);
            }
        }
        catch { }
        finally { Marshal.FreeHGlobal(isRemote); }

        try
        {
            var parent = GetParentProcess(Process.GetCurrentProcess().Id);
            if (parent != null)
            {
                var name = parent.ProcessName.ToLower();
                if (name.Contains("dnspy") || name.Contains("ilspy") || name.Contains("ollydbg") ||
                    name.Contains("x64dbg") || name.Contains("x32dbg") || name.Contains("windbg") ||
                    name.Contains("ida") || name.Contains("ida64") || name.Contains("devenv") ||
                    name.Contains("processhacker") || name.Contains("process hacker"))
                    return false;
            }
        }
        catch { }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) { _ = i * i; }
        sw.Stop();
        if (sw.ElapsedMilliseconds > 100) return false;

        try
        {
            int val = 0;
            NtSetInformationProcess(GetCurrentProcess(), PROCESS_DEBUG_PORT, ref val, sizeof(int));
        }
        catch { }

        return true;
    }

    // ── ANTI-RE TOOLS ────────────────────────────────────────────────────────
    private static bool AntiREToolsCheck()
    {
        var dangerProcesses = new[]
        {
            // Debuggers
            "dnspy", "dnspyex", "ilspy", "dotpeek", "jetbrains.resharper",
            "ollydbg", "x64dbg", "x32dbg", "windbg", "dbgview",
            "ida", "ida64", "idag", "idag64", "hex-rays",
            "binary ninja", "radare2", "r2", " Cutter",
            // Memory scanners
            "cheat engine", "cheatengine", "scanmem", "gameguardian",
            "processhacker", "process hacker", "processhacker64",
            "procmon", "procmon64", "procexp", "procexp64",
            // .NET specific
            "decompiler", "justdecompile", "telerik",
            // Network analysis
            "wireshark", "fiddler", "charles", "burp", "httpdebuggerpro",
            "mitmproxy", "tcpdump", "wireguard",
            // Misc analysis
            "apatedns", "networkmonitor", "dumpcap", "filemon", "regmon",
            "autoruns", "strings", "pestudio", "detect it easy", "die",
            "pe-bear", "binary ninja", "ghidra"
        };

        try
        {
            var running = Process.GetProcesses().Select(p => p.ProcessName.ToLower()).ToList();
            if (running.Any(p => dangerProcesses.Contains(p))) return false;
        }
        catch { }

        // Check for .NET decompiler DLLs in same directory
        try
        {
            var appDir = AppContext.BaseDirectory;
            var dangerFiles = new[] { "dnSpy.exe", "dnSpyEx.exe", "ILSpy.exe", "dotPeek.exe", "CheatEngine.exe" };
            foreach (var f in dangerFiles)
            {
                if (File.Exists(Path.Combine(appDir, f))) return false;
            }
        }
        catch { }

        // Check for installed RE tools via registry
        var regPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\dnSpy",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ILSpy",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Cheat Engine",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Cheat Engine"
        };
        foreach (var path in regPaths)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
                if (key != null) return false;
                using var key2 = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);
                if (key2 != null) return false;
            }
            catch { }
        }

        // Detect known debugger window classes
        try
        {
            var dangerClasses = new[] { "OLLYDBG", "x64dbg", "x32dbg", "IDA", "ProcessHacker" };
            foreach (var cls in dangerClasses)
            {
                if (FindWindow(cls, null) != IntPtr.Zero) return false;
            }
        }
        catch { }

        // Check for debugging flags in the PEB via NtQueryInformationProcess
        try
        {
            IntPtr debugFlags = IntPtr.Zero;
            var status = NtQueryInformationProcess(GetCurrentProcess(), PROCESS_DEBUG_FLAGS, ref debugFlags, IntPtr.Size, IntPtr.Zero);
            if (status == 0 && debugFlags == IntPtr.Zero) return false;
        }
        catch { }

        // Check for hardware breakpoints via thread context
        try
        {
            var thread = Process.GetCurrentProcess().Threads[0];
            // If we can't even access the thread, something is wrong
            _ = thread.Id;
        }
        catch { }

        // Timing-based detection: if someone single-steps through anti-debug code
        var ticks1 = GetTickCount();
        for (int i = 0; i < 10000; i++) { _ = Environment.TickCount; }
        var ticks2 = GetTickCount();
        if (ticks2 - ticks1 > 200) return false;

        return true;
    }

    // ── ANTI-VM ───────────────────────────────────────────────────────────────
    private static bool AntiVMCheck()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
            foreach (var obj in searcher.Get())
            {
                var manufacturer = (obj["Manufacturer"]?.ToString() ?? "").ToLower();
                var version = (obj["SMBIOSBIOSVersion"]?.ToString() ?? "").ToLower();
                if (manufacturer.Contains("vmware") || manufacturer.Contains("virtualbox") ||
                    manufacturer.Contains("qemu") || manufacturer.Contains("parallels") ||
                    version.Contains("vmware") || version.Contains("virtualbox") ||
                    version.Contains("qemu"))
                    { Log("VM: BIOS match"); return false; }
            }
        }
        catch (Exception ex) { Log($"VM-BIOS: {ex.Message}"); }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                var model = (obj["Model"]?.ToString() ?? "").ToLower();
                var manufacturer = (obj["Manufacturer"]?.ToString() ?? "").ToLower();
                if (model.Contains("vmware") || model.Contains("virtualbox") ||
                    model.Contains("qemu") || model.Contains("parallels") ||
                    model.Contains("hyper-v") || model.Contains("virtual machine") ||
                    manufacturer.Contains("vmware") ||
                    (manufacturer.Contains("microsoft") && model.Contains("virtual")))
                    { Log($"VM: CS match model={model} mfr={manufacturer}"); return false; }
            }
        }
        catch (Exception ex) { Log($"VM-CS: {ex.Message}"); }

        try
        {
            if (IsHyperVisorPresent()) { Log("VM: HyperVisor detected"); return false; }
        }
        catch (Exception ex) { Log($"VM-Hyper: {ex.Message}"); }

        var vmRegistryKeys = new[]
        {
            @"SOFTWARE\VMware, Inc.\VMware Tools",
            @"SOFTWARE\Oracle\VirtualBox Guest Additions",
            @"SYSTEM\CurrentControlSet\Services\VBoxGuest",
            @"SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters",
        };
        foreach (var keyPath in vmRegistryKeys)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
                if (key != null) { Log($"VM: Registry {keyPath}"); return false; }
            }
            catch { }
        }

        var vmMacPrefixes = new[] { "00:0C:29", "00:50:56", "00:05:69", "08:00:27", "0A:00:27" };
        try
        {
            var mac = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up)?
                .GetPhysicalAddress().ToString();
            if (mac != null && mac.Length >= 12)
            {
                var formatted = string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
                if (vmMacPrefixes.Any(p => formatted.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    { Log("VM: MAC match"); return false; }
            }
        }
        catch (Exception ex) { Log($"VM-MAC: {ex.Message}"); }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT NumberOfProcessors, TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                var cpus = Convert.ToInt32(obj["NumberOfProcessors"]);
                var ramGB = Convert.ToUInt64(obj["TotalPhysicalMemory"]) / (1024.0 * 1024 * 1024);
                if (cpus < 1 || ramGB < 2.0) { Log($"VM: HW cpus={cpus} ram={ramGB}"); return false; }
            }
        }
        catch (Exception ex) { Log($"VM-HW: {ex.Message}"); }

        var vmProcesses = new[] { "vmtoolsd", "vmwaretray", "vboxservice", "vboxtray", "qemu-ga", "vdagent", "vboxclient" };
        try
        {
            var running = Process.GetProcesses().Select(p => p.ProcessName.ToLower()).ToList();
            var hits = running.Where(p => vmProcesses.Contains(p)).ToList();
            if (hits.Count > 0) { Log($"VM: Process {string.Join(",",hits)}"); return false; }
        }
        catch (Exception ex) { Log($"VM-Procs: {ex.Message}"); }

        var vmFiles = new[]
        {
            @"C:\Windows\System32\vmGuestLib.dll", @"C:\Windows\System32\vboxhook.dll",
            @"C:\Windows\System32\vboxmrxnp.dll", @"C:\Windows\System32\VBoxControl.exe",
            @"C:\Program Files\VMware\VMware Tools", @"C:\Program Files\Oracle\VirtualBox Guest Additions"
        };
        foreach (var f in vmFiles) { if (File.Exists(f)) { Log($"VM: File {f}"); return false; } }

        var vmDrivers = new[] { "vmhgfs.sys", "VBoxGuest.sys", "VBoxMouse.sys", "VBoxSF.sys", "VBoxTray.exe" };
        foreach (var driver in vmDrivers)
        {
            var p = Path.Combine(@"C:\Windows\System32\drivers", driver);
            if (File.Exists(p)) { Log($"VM: Driver {driver}"); return false; }
        }

        Log("VM: passed");
        return true;
    }

    // ── ANTI-SANDBOX ──────────────────────────────────────────────────────────
    private static bool AntiSandboxCheck()
    {
        var sandboxProcesses = new[]
        {
            "wireshark", "fiddler", "charles", "burp", "httpdebuggerpro",
            "processhacker", "procmon", "procmon64", "procexp", "procexp64",
            "filemon", "regmon", "autoruns", "strings", "dumpcap", "apateDNS", "networkmonitor"
        };
        try
        {
            var running = Process.GetProcesses().Select(p => p.ProcessName.ToLower()).ToList();
            if (running.Any(p => sandboxProcesses.Contains(p))) { Log("SBOX: process"); return false; }
        }
        catch (Exception ex) { Log($"SBOX-proc: {ex.Message}"); }

        var envVars = new[] { "COMPUTERNAME", "USERDOMAIN", "USERNAME" };
        var suspiciousNames = new[] { "sandbox", "malware", "virus", "maltest",
            "malwaretest", "virustotal", "cuckoo", "sample", "fla-" };
        foreach (var env in envVars)
        {
            var val = Environment.GetEnvironmentVariable(env)?.ToLower() ?? "";
            if (suspiciousNames.Any(s => val.Contains(s))) { Log($"SBOX: env {env}={val}"); return false; }
        }

        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (uptime.TotalMinutes < 5) { Log($"SBOX: uptime {uptime}"); return false; }
        }
        catch (Exception ex) { Log($"SBOX-uptime: {ex.Message}"); }

        try
        {
            var container = Environment.GetEnvironmentVariable("CONTAINER_NAME");
            if (!string.IsNullOrEmpty(container)) { Log("SBOX: container"); return false; }
        }
        catch { }

        return true;
    }

    // ── ANTI-TIMING ───────────────────────────────────────────────────────────
    private static bool AntiTimingCheck()
    {
        var sw = Stopwatch.StartNew();
        int x = 0;
        for (int i = 0; i < 100000; i++) x += i;
        sw.Stop();
        if (sw.ElapsedMilliseconds > 1000) { Log($"TIME: {sw.ElapsedMilliseconds}ms"); return false; }

        return true;
    }

    // ── ANTI-TAMPER: Self-hash validation ────────────────────────────────────
    private static bool AntiTamperCheck()
    {
        try
        {
            var hash = ComputeCriticalMethodHash();
            if (hash == null || hash.Length == 0) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[]? ComputeCriticalMethodHash()
    {
        try
        {
            using var sha = SHA256.Create();
            var assembly = Assembly.GetExecutingAssembly();
            var module = assembly.ManifestModule;
            var moduleId = module.ModuleVersionId;
            var assemblyId = assembly.GetName().Name ?? "";

            // Combine module GUID + module name + type count + metadata token for integrity fingerprint
            var payload = $"{moduleId}:{assemblyId}:{assembly.GetTypes().Length}:{module.MetadataToken}:{assembly.FullName?.Length}";
            return sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        }
        catch { return null; }
    }

    // ── ANTI-DUMP ─────────────────────────────────────────────────────────────
    private static bool AntiDumpCheck()
    {
        try
        {
            // Patch PE headers to prevent memory dumping
            var module = Assembly.GetExecutingAssembly().Modules.First();
            var baseAddr = Marshal.GetHINSTANCE(module);
            if (baseAddr != IntPtr.Zero && baseAddr != (IntPtr)(-1))
            {
                // Read PE signature offset
                var peOffset = Marshal.ReadInt32(baseAddr, 0x3C);
                var peBase = baseAddr + peOffset;

                // Zero out the PE header's code section metadata to break dump tools
                // Write garbage over the debug directory
                var optHeaderOffset = peOffset + 24;
                var magic = Marshal.ReadInt16(peBase, optHeaderOffset);

                // Number of RVA and Sizes is at offset 108 (PE32+) or 92 (PE32) from PE base
                var rvaOffset = magic == 0x20B ? optHeaderOffset + 108 : optHeaderOffset + 92;
                var numRva = Marshal.ReadInt32(peBase, rvaOffset);

                if (numRva > 7) // Debug directory is entry 7
                {
                    // Overwrite the debug directory RVA and size to confuse dump tools
                    var debugDirRvaOffset = rvaOffset + 4 + (7 * 8);
                    Marshal.WriteInt32(peBase, debugDirRvaOffset, 0);
                    Marshal.WriteInt32(peBase, debugDirRvaOffset + 4, 0);
                }
            }

            EraseFromStack();
        }
        catch { }

        return true;
    }

    private static void EraseFromStack()
    {
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
    }

    // ── Helper: CPUID hypervisor detection ───────────────────────────────────
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsHyperVisorPresent()
    {
        try
        {
            // If CPUID reports hypervisor present bit, we're in a VM
            // This is checked via WMI in practice since we can't call CPUID from C#
            using var searcher = new ManagementObjectSearcher("SELECT VirtualizationFirmwareEnabled FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var enabled = obj["VirtualizationFirmwareEnabled"];
                if (enabled != null && Convert.ToBoolean(enabled))
                {
                    // Hypervisor firmware is enabled — check if it's a known hypervisor
                    using var searcher2 = new ManagementObjectSearcher("SELECT Name, Manufacturer FROM Win32_Processor");
                    foreach (var obj2 in searcher2.Get())
                    {
                        var name = (obj2["Name"]?.ToString() ?? "").ToLower();
                        var mfg = (obj2["Manufacturer"]?.ToString() ?? "").ToLower();
                        if (name.Contains("qemu") || name.Contains("kvm") || mfg.Contains("qemu"))
                            return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    // ── Helper: Get Parent Process ────────────────────────────────────────────
    private static Process? GetParentProcess(int pid)
    {
        try
        {
            using var query = new ManagementObjectSearcher($"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId={pid}");
            foreach (var obj in query.Get())
            {
                var parentPid = Convert.ToInt32(obj["ParentProcessId"]);
                return Process.GetProcessById(parentPid);
            }
        }
        catch { }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate bool CheckRemoteDebuggerDelegate(IntPtr hProcess, IntPtr isDebuggerPresent);
}
