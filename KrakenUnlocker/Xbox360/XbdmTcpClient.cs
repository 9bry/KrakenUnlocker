using System.Net.Sockets;
using System.Text;
using System.IO;

namespace KrakenUnlocker.Xbox360
{
    public static class XbdmTcpClient
    {
        private const int Port = 730;
        private const int ReadTimeoutMs = 3000;
        private const int BufSize = 65536;

        public static (bool ok, string response) SendCommand(string ip, string command)
        {
            try
            {
                using var tcp = new TcpClient { SendTimeout = 3000 };
                tcp.Connect(ip, Port);
                if (!tcp.Connected) return (false, "Could not connect");

                var stream = tcp.GetStream();
                stream.ReadTimeout = ReadTimeoutMs;

                // Send command
                var cmdBytes = Encoding.ASCII.GetBytes(command + "\r\n");
                stream.Write(cmdBytes, 0, cmdBytes.Length);

                // Read response — loop until timeout (server holds connection open)
                var buf = new byte[BufSize];
                int total = 0;
                try
                {
                    while (total < BufSize)
                    {
                        int read = stream.Read(buf, total, BufSize - total);
                        if (read <= 0) break;
                        total += read;
                        // If we got data, wait briefly for more (multi-line responses)
                        System.Threading.Thread.Sleep(50);
                    }
                }
                catch (IOException)
                {
                    // Read timed out = normal, server keeps connection alive
                }

                if (total == 0) return (true, "(empty response)");

                var resp = Encoding.ASCII.GetString(buf, 0, total).Trim('\0', '\r', '\n', ' ', '\t');
                return (true, resp);
            }
            catch (Exception ex)
            {
                return (false, $"TCP error: {ex.Message}");
            }
        }
    }
}