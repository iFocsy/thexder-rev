using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Serilog;
using Thexder.Core.Game;

namespace Thexder.Services.Network
{
    /// <summary>A message delivered to the game: who sent it and its bytes.</summary>
    internal readonly record struct NetMessage(int From, byte[] Data);

    /// <summary>A player in the session.</summary>
    internal sealed record NetPlayer(int Id, string Name);

    /// <summary>Ids are 1 for the host and count up for each joiner.</summary>
    internal sealed class NetSession : INetLink, IDisposable
    {
        public const int DefaultPort = 47624; // DirectPlay's own port, for old times' sake
        public const int AllPlayers = 0;
        private const int ProtocolVersion = 1;
        private const int MaxMessage = 1 << 20;

        private enum Kind : byte
        {
            Hello = 1, // Client -> host: version, player name
            Welcome = 2, // Host -> client: your id, session name, max players, player list
            Refused = 3, // Host -> client: reason
            Joined = 4, // Player id + name
            Left = 5, // Player id
            Data = 6, // From, to, payload
        }

        private readonly ConcurrentQueue<NetMessage> inbox = new();
        private readonly ConcurrentQueue<Action> events = new();
        private readonly Dictionary<int, NetPlayer> players = new();
        private readonly Dictionary<int, Peer> peers = new(); // Host: one per client; client: the host as id 1
        private readonly object gate = new();
        private readonly CancellationTokenSource stop = new();
        private TcpListener? listener;
        private int nextId = 2;
        private bool disposed;

        private NetSession(bool isHost, string sessionName, int maxPlayers)
        {
            IsHost = isHost;
            SessionName = sessionName;
            MaxPlayers = maxPlayers;
        }

        public bool IsHost { get; }

        /// <summary>Identifies this session on the network (DirectPlay's session instance GUID).</summary>
        public Guid Instance { get; } = Guid.NewGuid();

        public string SessionName { get; private set; }

        public int MaxPlayers { get; private set; }

        public int LocalId { get; private set; }

        public int Port { get; private set; }

        /// <summary>Host: whether new players may join.</summary>
        public bool Accepting { get; set; } = true;

        /// <summary>False once the connection to the session is gone (for a client: the host left).</summary>
        public bool Connected { get; private set; } = true;

        /// <summary>A player joined (raised from <see cref="Poll"/> on the game's thread).</summary>
        public event Action<NetPlayer>? PlayerJoined;

        /// <summary>A player left (raised from <see cref="Poll"/>).</summary>
        public event Action<NetPlayer>? PlayerLeft;

        /// <summary>
        /// The session ended for this player (host gone or the connection broke; raised from <see cref="Poll"/>).
        /// </summary>
        public event Action<string>? Disconnected;

        public IReadOnlyList<NetPlayer> Players
        {
            get
            {
                lock (gate)
                {
                    return players.Values.OrderBy(p => p.Id).ToList();
                }
            }
        }

        /// <summary>
        /// Creates a session and listens for players (on every network, or only on <paramref name="bind"/>).
        /// </summary>
        public static NetSession Host(string sessionName, string playerName, int maxPlayers, int port = DefaultPort, IPAddress? bind = null)
        {
            var session = new NetSession(true, sessionName, maxPlayers) { LocalId = 1 };
            session.players[1] = new NetPlayer(1, playerName);
            session.listener = new TcpListener(bind ?? IPAddress.Any, port);
            session.listener.Start();
            session.Port = ((IPEndPoint)session.listener.LocalEndpoint).Port;
            _ = Task.Run(session.AcceptLoop);
            Log.Information("Hosting network game {Session} on port {Port}", sessionName, session.Port);
            return session;
        }

        /// <summary>Joins the session at <paramref name="host"/>; throws when it cannot be reached or refuses.</summary>
        public static async Task<NetSession> JoinAsync(IPEndPoint host, string playerName, CancellationToken cancel = default)
        {
            var client = new TcpClient { NoDelay = true };
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await client.ConnectAsync(host, timeout.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                await WriteFrameAsync(stream, Kind.Hello, w =>
                {
                    w.Write(ProtocolVersion);
                    w.Write(playerName);
                }, timeout.Token).ConfigureAwait(false);
                var (kind, body) = await ReadFrameAsync(stream, timeout.Token).ConfigureAwait(false);
                using var r = new BinaryReader(new MemoryStream(body), Encoding.UTF8);
                if (kind == Kind.Refused)
                {
                    throw new InvalidOperationException(r.ReadString());
                }
                if (kind != Kind.Welcome)
                {
                    throw new InvalidDataException("Unexpected answer from the host.");
                }
                var session = new NetSession(false, "", 0) { LocalId = r.ReadInt32(), Port = host.Port };
                session.SessionName = r.ReadString();
                session.MaxPlayers = r.ReadInt32();
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var p = new NetPlayer(r.ReadInt32(), r.ReadString());
                    session.players[p.Id] = p;
                }
                var peer = new Peer(1, client);
                session.peers[1] = peer;
                _ = Task.Run(() => session.ReadLoop(peer));
                Log.Information("Joined network game {Session} as player {Id}", session.SessionName, session.LocalId);
                return session;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        /// <summary>Sends to one player or to <see cref="AllPlayers"/> (not to oneself).</summary>
        public void Send(int to, ReadOnlySpan<byte> payload)
        {
            if (!Connected || disposed)
            {
                return;
            }
            byte[] data = payload.ToArray();
            if (IsHost)
            {
                Route(LocalId, to, data);
            }
            else if (peers.TryGetValue(1, out var host))
            {
                host.Send(Kind.Data, w =>
                {
                    w.Write(LocalId);
                    w.Write(to);
                    w.Write(data.Length);
                    w.Write(data);
                });
            }
        }

        /// <summary>Raises the queued join/leave/disconnect events on the caller's thread (call once per frame).</summary>
        public void Poll()
        {
            while (events.TryDequeue(out var e))
            {
                e();
            }
        }

        /// <summary>The next received message, if any (IDirectPlay::Receive).</summary>
        public bool TryReceive(out NetMessage message) => inbox.TryDequeue(out message);

        bool INetLink.TryReceive(out int from, out byte[] data)
        {
            if (inbox.TryDequeue(out var message))
            {
                (from, data) = (message.From, message.Data);
                return true;
            }
            (from, data) = (0, Array.Empty<byte>());
            return false;
        }

        public string? PlayerName(int id)
        {
            lock (gate)
            {
                return players.TryGetValue(id, out var p) ? p.Name : null;
            }
        }

        // ---- Host side -----------------------------------------------------------------------------------

        private async Task AcceptLoop()
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener!.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                }
                catch (Exception) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (SocketException ex)
                {
                    Log.Warning(ex, "Accept failed");
                    continue;
                }
                client.NoDelay = true;
                _ = Task.Run(() => Admit(client));
            }
        }

        private async Task Admit(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var (kind, body) = await ReadFrameAsync(stream, timeout.Token).ConfigureAwait(false);
                using var r = new BinaryReader(new MemoryStream(body), Encoding.UTF8);
                if (kind != Kind.Hello || r.ReadInt32() != ProtocolVersion)
                {
                    await WriteFrameAsync(stream, Kind.Refused, w => w.Write("This Thexder version cannot join that game."), timeout.Token).ConfigureAwait(false);
                    client.Dispose();
                    return;
                }
                string name = r.ReadString();
                Peer? peer = null;
                lock (gate)
                {
                    if (players.Count < MaxPlayers && Accepting)
                    {
                        // The welcome is queued before the peer can receive relayed traffic.
                        int id = nextId++;
                        players[id] = new NetPlayer(id, name);
                        peer = new Peer(id, client);
                        var list = players.Values.OrderBy(p => p.Id).ToList();
                        peer.Send(Kind.Welcome, w =>
                        {
                            w.Write(id);
                            w.Write(SessionName);
                            w.Write(MaxPlayers);
                            w.Write(list.Count);
                            foreach (var p in list)
                            {
                                w.Write(p.Id);
                                w.Write(p.Name);
                            }
                        });
                        peers[id] = peer;
                    }
                }
                if (peer == null)
                {
                    await WriteFrameAsync(stream, Kind.Refused, w => w.Write(Accepting ? "The game is full." : "The game is not taking new players."), timeout.Token).ConfigureAwait(false);
                    client.Dispose();
                    return;
                }
                var joined = new NetPlayer(peer.Id, name);
                foreach (var other in OtherPeers(peer.Id))
                {
                    other.Send(Kind.Joined, w =>
                    {
                        w.Write(joined.Id);
                        w.Write(joined.Name);
                    });
                }
                events.Enqueue(() => PlayerJoined?.Invoke(joined));
                Log.Information("Player {Id} {Name} joined", joined.Id, name);
                await ReadLoop(peer).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or InvalidDataException)
            {
                client.Dispose();
            }
        }

        private List<Peer> OtherPeers(int except)
        {
            lock (gate)
            {
                return peers.Values.Where(p => p.Id != except).ToList();
            }
        }

        // Host: deliver a message from 'from' to 'to' (or everybody but the sender).
        private void Route(int from, int to, byte[] data)
        {
            if ((to == AllPlayers || to == LocalId) && from != LocalId)
            {
                inbox.Enqueue(new NetMessage(from, data));
            }
            foreach (var peer in OtherPeers(from))
            {
                if (to == AllPlayers || to == peer.Id)
                {
                    peer.Send(Kind.Data, w =>
                    {
                        w.Write(from);
                        w.Write(to);
                        w.Write(data.Length);
                        w.Write(data);
                    });
                }
            }
        }

        // ---- Both sides ----------------------------------------------------------------------------------

        private async Task ReadLoop(Peer peer)
        {
            string reason = "The connection was lost.";
            try
            {
                var stream = peer.Client.GetStream();
                while (!stop.IsCancellationRequested)
                {
                    var (kind, body) = await ReadFrameAsync(stream, stop.Token).ConfigureAwait(false);
                    using var r = new BinaryReader(new MemoryStream(body), Encoding.UTF8);
                    switch (kind)
                    {
                        case Kind.Data:
                        {
                            int from = r.ReadInt32(), to = r.ReadInt32();
                            byte[] data = r.ReadBytes(r.ReadInt32());
                            if (IsHost)
                            {
                                Route(peer.Id, to, data); // A client can only speak for itself
                            }
                            else
                            {
                                inbox.Enqueue(new NetMessage(from, data));
                            }
                            break;
                        }
                        case Kind.Joined when !IsHost:
                        {
                            var p = new NetPlayer(r.ReadInt32(), r.ReadString());
                            lock (gate)
                            {
                                players[p.Id] = p;
                            }
                            events.Enqueue(() => PlayerJoined?.Invoke(p));
                            break;
                        }
                        case Kind.Left when !IsHost:
                        {
                            int id = r.ReadInt32();
                            RemovePlayer(id);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or InvalidDataException or EndOfStreamException)
            {
                if (ex is InvalidDataException)
                {
                    reason = ex.Message;
                }
            }
            finally
            {
                peer.Dispose();
                if (!stop.IsCancellationRequested)
                {
                    if (IsHost)
                    {
                        lock (gate)
                        {
                            peers.Remove(peer.Id);
                        }
                        foreach (var other in OtherPeers(peer.Id))
                        {
                            other.Send(Kind.Left, w => w.Write(peer.Id));
                        }
                        RemovePlayer(peer.Id);
                    }
                    else
                    {
                        Connected = false;
                        events.Enqueue(() => Disconnected?.Invoke(reason));
                    }
                }
            }
        }

        private void RemovePlayer(int id)
        {
            NetPlayer? gone;
            lock (gate)
            {
                players.Remove(id, out gone);
            }
            if (gone != null)
            {
                events.Enqueue(() => PlayerLeft?.Invoke(gone));
                Log.Information("Player {Id} {Name} left", gone.Id, gone.Name);
            }
        }

        private static async Task WriteFrameAsync(NetworkStream stream, Kind kind, Action<BinaryWriter> body, CancellationToken cancel)
        {
            byte[] frame = BuildFrame(kind, body);
            await stream.WriteAsync(frame, cancel).ConfigureAwait(false);
        }

        private static byte[] BuildFrame(Kind kind, Action<BinaryWriter> body)
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(0);
                w.Write((byte)kind);
                body(w);
            }
            byte[] frame = ms.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length - 4);
            return frame;
        }

        private static async Task<(Kind Kind, byte[] Body)> ReadFrameAsync(NetworkStream stream, CancellationToken cancel)
        {
            byte[] header = new byte[5];
            await stream.ReadExactlyAsync(header, cancel).ConfigureAwait(false);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length < 1 || length > MaxMessage)
            {
                throw new InvalidDataException("Bad message from the network.");
            }
            byte[] body = new byte[length - 1];
            await stream.ReadExactlyAsync(body, cancel).ConfigureAwait(false);
            return ((Kind)header[4], body);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Connected = false;
            stop.Cancel();
            listener?.Stop();
            lock (gate)
            {
                foreach (var peer in peers.Values)
                {
                    peer.Dispose();
                }
                peers.Clear();
            }
        }

        /// <summary>One TCP connection; sends are serialized through a queue drained by one writer.</summary>
        private sealed class Peer : IDisposable
        {
            private readonly BlockingCollection<byte[]> outbox = new();

            public Peer(int id, TcpClient client)
            {
                Id = id;
                Client = client;
                var writer = new Thread(WriteLoop) { IsBackground = true, Name = $"Net peer {id}" };
                writer.Start();
            }

            public int Id { get; }

            public TcpClient Client { get; }

            public void Send(Kind kind, Action<BinaryWriter> body)
            {
                if (!outbox.IsAddingCompleted)
                {
                    try
                    {
                        outbox.Add(BuildFrame(kind, body));
                    }
                    catch (InvalidOperationException)
                    {
                        // Closed meanwhile
                    }
                }
            }

            private void WriteLoop()
            {
                try
                {
                    var stream = Client.GetStream();
                    foreach (byte[] frame in outbox.GetConsumingEnumerable())
                    {
                        stream.Write(frame);
                    }
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
                {
                    Client.Dispose();
                }
            }

            public void Dispose()
            {
                outbox.CompleteAdding();
                Client.Dispose();
            }
        }
    }
}
