using System.Text;

namespace KrakenUnlocker.Xbox360
{
    public static class ConsoleFeaturesRpc
    {
        public static string HexEncode(string text) =>
            string.Concat(Encoding.ASCII.GetBytes(text).Select(b => b.ToString("X2")));

        public static string BuildResolveParams(string moduleName, int ordinal)
        {
            var hex = HexEncode(moduleName);
            return $"A\\0\\A\\2\\2/{moduleName.Length}\\{hex}\\1\\{ordinal}\\";
        }

        public static string BuildCallParams(uint address, params object[] args)
        {
            var sb = new StringBuilder();
            sb.Append($"A\\{address:X8}\\A\\{args.Length}");
            foreach (var arg in args)
            {
                if (arg is int intVal)
                    sb.Append($"\\1\\{intVal}");
                else if (arg is uint uintVal)
                    sb.Append($"\\1\\{uintVal}");
                else if (arg is long longVal)
                    sb.Append($"\\1\\{longVal}");
                else if (arg is string strVal)
                {
                    var hex = HexEncode(strVal);
                    sb.Append($"\\7/{strVal.Length}\\{hex}");
                }
                else
                    throw new ArgumentException($"Unsupported param type: {arg.GetType()}");
            }
            sb.Append('\\');
            return sb.ToString();
        }

        public static bool TryParseResolveResponse(string response, out uint address)
        {
            address = 0;
            var m = System.Text.RegularExpressions.Regex.Match(
                response, @"\b([0-9A-Fa-f]{8})\b");
            if (m.Success)
            {
                address = System.Convert.ToUInt32(m.Groups[1].Value, 16);
                return true;
            }
            return false;
        }

        public static string BuildResolveCommand(string moduleName, int ordinal)
        {
            var p = BuildResolveParams(moduleName, ordinal);
            return $"consolefeatures ver=2 type=9 params=\"{p}\"";
        }

        public static string BuildCallCommand(uint address, params object[] args)
        {
            var p = BuildCallParams(address, args);
            return $"consolefeatures ver=2 type=1 system as=0 params=\"{p}\"";
        }

        public static string BuildCallCommandGame(uint address, params object[] args)
        {
            var p = BuildCallParams(address, args);
            return $"consolefeatures ver=2 type=1 as=0 params=\"{p}\"";
        }
    }
}
