using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace KrakenUnlocker.Xbox360
{
    public static class NativeLoader
    {
        private static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;

            string extractDir = Path.Combine(Path.GetTempPath(), "KrakenXbdm");
            Directory.CreateDirectory(extractDir);

            ExtractFromResources(extractDir);
            SetDllDirectory(extractDir);
            _loaded = true;
        }

        private static void ExtractFromResources(string dir)
        {
            var asm = Assembly.GetExecutingAssembly();
            string[] dlls = ["xbdm.dll", "msvcp71.dll", "msvcr71.dll"];

            foreach (var dll in dlls)
            {
                string target = Path.Combine(dir, dll);
                if (File.Exists(target)) continue;

                string resourceName = $"KrakenUnlocker.xbdm.{dll}";
                var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    resourceName = $"{asm.GetName().Name}.xbdm.{dll}";
                    stream = asm.GetManifestResourceStream(resourceName);
                }
                if (stream == null) continue;
                using (stream)
                {
                    using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
                    stream.CopyTo(fs);
                }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string? lpPathName);
    }
}
