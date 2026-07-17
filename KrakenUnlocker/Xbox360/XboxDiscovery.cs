using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace KrakenUnlocker.Xbox360
{
    public class DiscoveredConsole
    {
        public string Name { get; set; } = "";
        public string Ip { get; set; } = "";
        public override string ToString() => $"{Name} ({Ip})";
    }

    public static class XboxDiscovery
    {
        private const int DiscoveryPort = 731;
        private const int TimeoutMs = 2000;

        public static DiscoveredConsole[] DiscoverAll()
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            udp.Client.ReceiveTimeout = TimeoutMs;
            byte[] request = Encoding.ASCII.GetBytes("discover");
            udp.Send(request, request.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

            var consoles = new List<DiscoveredConsole>();
            try
            {
                while (true)
                {
                    IPEndPoint? remote = null;
                    byte[] resp = udp.Receive(ref remote);
                    if (remote == null) continue;
                    var parsed = ParseResponse(Encoding.ASCII.GetString(resp));
                    if (parsed != null)
                        consoles.Add(parsed);
                }
            }
            catch { }
            return consoles.ToArray();
        }

        public static DiscoveredConsole? DiscoverByName(string consoleName)
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            udp.Client.ReceiveTimeout = TimeoutMs;
            byte[] request = Encoding.ASCII.GetBytes("discover");
            udp.Send(request, request.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

            try
            {
                while (true)
                {
                    IPEndPoint? remote = null;
                    byte[] resp = udp.Receive(ref remote);
                    if (remote == null) continue;
                    var parsed = ParseResponse(Encoding.ASCII.GetString(resp));
                    if (parsed != null &&
                        parsed.Name.Equals(consoleName, StringComparison.OrdinalIgnoreCase))
                        return parsed;
                }
            }
            catch { }
            return null;
        }

        private static DiscoveredConsole? ParseResponse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            // Expected format: "discover <ip> dbgname=\"ConsoleName\""
            var match = Regex.Match(text, @"discover\s+([^\s]+)\s+dbgname=""([^""]+)""");
            if (!match.Success) return null;
            return new DiscoveredConsole
            {
                Ip = match.Groups[1].Value,
                Name = match.Groups[2].Value
            };
        }
    }
}
