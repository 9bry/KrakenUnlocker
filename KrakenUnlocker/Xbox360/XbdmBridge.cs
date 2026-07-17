using System.Runtime.InteropServices;
using System.Text;

namespace KrakenUnlocker.Xbox360
{
    public static class XbdmBridge
    {
        private const string XbdmDll = "xbdm.dll";

        static XbdmBridge()
        {
            NativeLoader.EnsureLoaded();
        }

        public const uint XBDM_NOERR = 0x02DA0000;
        public const uint XBDM_CONNECTED = 0x02DA0001;

        // ── Connection ─────────────────────────────────────────────────────────
        [DllImport(XbdmDll, CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern uint DmGetNameOfXbox([Out] StringBuilder name, ref uint size, bool resolveable);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern uint DmGetAltAddress(out uint address);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmSetXboxName([MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmResolveXboxName(out uint address);

        [DllImport(XbdmDll)]
        public static extern uint DmOpenConnection(out IntPtr connection);

        [DllImport(XbdmDll)]
        public static extern uint DmCloseConnection(IntPtr connection);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmGetAvailableXboxes(out IntPtr list);

        // ── System Info ────────────────────────────────────────────────────────
        [DllImport(XbdmDll)]
        public static extern uint DmGetSystemInfo(out DM_SYSTEM_INFO info);

        [DllImport(XbdmDll)]
        public static extern uint DmGetSystemTime(out long time);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmGetConsoleType(out uint type);

        // ── Drives / Files ─────────────────────────────────────────────────────
        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmGetDriveList([Out] byte[] buffer, ref uint size);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmOpenDir([MarshalAs(UnmanagedType.LPStr)] string dir, out IntPtr handle);

        [DllImport(XbdmDll)]
        public static extern uint DmWalkDir(IntPtr handle, out DM_FILE_ATTRIBUTES fileInfo);

        [DllImport(XbdmDll)]
        public static extern uint DmCloseDir(IntPtr handle);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmGetFileAttributes(
            [MarshalAs(UnmanagedType.LPStr)] string fileName,
            out DM_FILE_ATTRIBUTES fileInfo);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmReceiveFileA(
            [MarshalAs(UnmanagedType.LPStr)] string localFile,
            [MarshalAs(UnmanagedType.LPStr)] string remoteFile);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmSendFileA(
            [MarshalAs(UnmanagedType.LPStr)] string localFile,
            [MarshalAs(UnmanagedType.LPStr)] string remoteFile);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmDeleteFile(
            [MarshalAs(UnmanagedType.LPStr)] string fileName,
            bool isDirectory);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmMkdir([MarshalAs(UnmanagedType.LPStr)] string dir);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmRenameFile(
            [MarshalAs(UnmanagedType.LPStr)] string oldName,
            [MarshalAs(UnmanagedType.LPStr)] string newName);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmScreenShot([MarshalAs(UnmanagedType.LPStr)] string filename);

        // ── Console control ────────────────────────────────────────────────────
        [DllImport(XbdmDll)]
        public static extern uint DmReboot(uint flags);

        [DllImport(XbdmDll)]
        public static extern uint DmGo();

        [DllImport(XbdmDll)]
        public static extern uint DmStop();

        // ── XBE / Title info ───────────────────────────────────────────────────
        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmGetXbeInfo([MarshalAs(UnmanagedType.LPStr)] string xbePath, out DM_XBE_INFO info);

        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmSetTitle(
            [MarshalAs(UnmanagedType.LPStr)] string directory,
            [MarshalAs(UnmanagedType.LPStr)] string commandLine,
            [MarshalAs(UnmanagedType.LPStr)] string xbeFileName);

        // ── Raw command (useful for achievement unlock via XBLA) ───────────────
        // Use IntPtr instead of StringBuilder to avoid marshaling crashes
        [DllImport(XbdmDll, CharSet = CharSet.Ansi)]
        public static extern uint DmSendCommand(
            [MarshalAs(UnmanagedType.LPStr)] string command,
            IntPtr response,
            uint responseSize);

        /// <summary>Send a command and return HR + trimmed response</summary>
        public static (uint hr, string response) SendCommandRaw(string command)
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                buf = Marshal.AllocHGlobal(16384);
                uint hr = DmSendCommand(command, buf, 16384);
                string resp = Marshal.PtrToStringAnsi(buf) ?? "";
                return (hr, resp.Trim('\0', '\r', '\n', ' ', '\t'));
            }
            catch (Exception ex)
            {
                return (0xFFFFFFFF, $"EXCEPTION: {ex.Message}");
            }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
        }
    }

    // ── Structs ─────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DM_FILE_ATTRIBUTES
    {
        public uint CreationLow;
        public uint CreationHigh;
        public uint ChangeTimeLow;
        public uint ChangeTimeHigh;
        public uint SizeLow;
        public uint SizeHigh;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DM_SYSTEM_INFO
    {
        public uint BaseKernelVersion;
        public uint KernelVersion;
        public uint XDKVersion;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DM_XBE_INFO
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string LaunchPath;
        public uint TimeStamp;
        public uint CheckSum;
        public uint StackSize;
    }

    public static class DmFileAttributes
    {
        public const uint Directory = 0x10;
        public const uint ReadOnly = 0x01;
        public const uint Hidden = 0x02;
        public const uint System = 0x04;
    }

    public static class DmRebootFlags
    {
        public const uint Cold = 0x00;
        public const uint Warm = 0x01;
        public const uint NoWait = 0x02;
        public const uint Stop = 0x04;
    }
}
