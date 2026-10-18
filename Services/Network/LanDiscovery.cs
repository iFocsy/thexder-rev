using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Serilog;

namespace Thexder.Services.Network
{
    /// <summary>A game found on the local network.</summary>
    internal sealed record NetSessionInfo(IPEndPoint Host, string Name, int Players, int MaxPlayers, Guid Instance, string Details = "");

    /// <summary>
    /// Finding games on the local network, like DirectPlay's EnumSessions: a joiner broadcasts a question on UDP and
    /// every host answers with its session name, player count and TCP port.
    /// </summary>
    internal static class LanDiscovery
    {
        private const string Question = "THEXDER-SESSIONS?3";
        private const string AnswerPrefix = "THEXDER-SESSION!3\n";

        /// <summary>
        /// Answers questions for <paramref name="session"/> until it is disposed or the token is cancelled.
        /// </summary>
        public static async Task AnswerAsync(NetSession session, int port, string details, CancellationToken cancel, IPAddress? bind = null)
        {
            UdpClient udp;
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(bind ?? IPAddress.Any, port));
            }
            catch (SocketException ex)
            {
                Log.Warning(ex, "LAN discovery is not available on UDP port {Port}", port);
                return;
            }
            using (udp)
            {
                while (!cancel.IsCancellationRequested && session.Connected)
                {
                    UdpReceiveResult request;
                    try
                    {
                        request = await udp.ReceiveAsync(cancel).ConfigureAwait(false);
                    }
                    catch (Exception) when (cancel.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        continue;
                    }
                    if (Encoding.ASCII.GetString(request.Buffer) != Question || !session.Accepting)
                    {
                        continue;
                    }
                    string answer = $"{AnswerPrefix}{session.Instance:N}\n{session.Port}\n{session.Players.Count}\n{session.MaxPlayers}\n{details.Replace('\n', ' ')}\n{session.SessionName}";
                    byte[] bytes = Encoding.UTF8.GetBytes(answer);
                    try
                    {
                        await udp.SendAsync(bytes, request.RemoteEndPoint, cancel).ConfigureAwait(false);
                    }
                    catch (SocketException)
                    {
                    }
                }
            }
        }

        /// <summary>Asks the local network for games and collects the answers for <paramref name="wait"/>.</summary>
        public static async Task<IReadOnlyList<NetSessionInfo>> FindAsync(int port, TimeSpan wait, CancellationToken cancel = default)
        {
            var found = new Dictionary<Guid, NetSessionInfo>();
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            byte[] question = Encoding.ASCII.GetBytes(Question);
            foreach (var target in BroadcastAddresses())
            {
                try
                {
                    await udp.SendAsync(question, new IPEndPoint(target, port), cancel).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                }
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(wait);
            while (true)
            {
                UdpReceiveResult answer;
                try
                {
                    answer = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    continue;
                }
                string text = Encoding.UTF8.GetString(answer.Buffer);
                if (!text.StartsWith(AnswerPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                string[] lines = text[AnswerPrefix.Length..].Split('\n', 6);
                if (lines.Length == 6 && Guid.TryParse(lines[0], out var instance) && int.TryParse(lines[1], out int tcpPort)
                    && int.TryParse(lines[2], out int players) && int.TryParse(lines[3], out int max))
                {
                    // A host answers once per network interface; the first answer that reached us is a usable address.
                    found.TryAdd(instance, new NetSessionInfo(new IPEndPoint(answer.RemoteEndPoint.Address, tcpPort), lines[5], players, max, instance, lines[4]));
                }
            }
            return found.Values.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // The limited broadcast plus every interface's subnet broadcast (some stacks only send the former on one NIC),
        // and the loopback address so a second copy on the same PC finds the host.
        private static IEnumerable<IPAddress> BroadcastAddresses()
        {
            var list = new List<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null)
                    {
                        continue;
                    }
                    byte[] ip = ua.Address.GetAddressBytes(), mask = ua.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        broadcast[i] = (byte)(ip[i] | ~mask[i]);
                    }
                    list.Add(new IPAddress(broadcast));
                }
            }
            return list.Distinct();
        }
    }
}
