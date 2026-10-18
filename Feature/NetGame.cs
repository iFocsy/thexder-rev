using System.Net;
using Serilog;
using Thexder.Core.Game;
using Thexder.Services.Network;

namespace Thexder.Feature
{
    /// <summary>
    /// The network session of the running game: hosts or joins over TCP, feeds joins and leaves to the game, answers LAN
    /// searches while hosting and closes everything when the game ends.
    /// </summary>
    internal sealed class NetGame : IDisposable
    {
        private readonly ThexderGame game;
        private NetSession? session;
        private CancellationTokenSource? answering;

        public NetGame(ThexderGame game)
        {
            this.game = game;
            game.NetEnded += _ => Close();
        }

        /// <summary>The session ended for this machine (the host left or the connection broke); the reason.</summary>
        public event Action<string>? Lost;

        public bool Connected => session != null;

        public bool IsHost => session?.IsHost == true;

        /// <summary>Create: hosts a game with these parameters on <paramref name="port"/> and builds its level.</summary>
        public void Host(string sessionName, NetGameParameters parameters, int port, bool loopbackOnly = false)
        {
            Close();
            var bind = loopbackOnly ? IPAddress.Loopback : null;
            var s = NetSession.Host(sessionName, game.PlayerName, maxPlayers: ThexderGame.NetSlots, port, bind);
            Attach(s);
            game.NetHost(s, parameters);
            answering = new CancellationTokenSource();
            string details = $"Game {parameters.GameNumber:00000} · {(parameters.CreateEnemies ? parameters.EnemyCount : 0)} enemies"
                + (parameters.ScoreLimit > 0 ? $" · to {parameters.ScoreLimit}" : "");
            _ = LanDiscovery.AnswerAsync(s, port, details, answering.Token, bind);
            Log.Information("Hosting network game {Name}, {Parameters}", sessionName, parameters);
        }

        /// <summary>Join: connects to a host; the game then waits for the host's parameters.</summary>
        public async Task JoinAsync(IPEndPoint host, CancellationToken cancel = default)
        {
            Close();
            var s = await NetSession.JoinAsync(host, game.PlayerName, cancel);
            Attach(s);
            game.NetJoin(s, s.Players.Select(p => (p.Id, p.Name)));
        }

        /// <summary>Disconnect: a host ends the game for everybody.</summary>
        public void Leave()
        {
            game.NetLeave();
            Close();
        }

        /// <summary>Once per rendered frame: session events, and the waiting client's handshake.</summary>
        public void Poll()
        {
            session?.Poll();
            game.NetPoll();
        }

        private void Attach(NetSession s)
        {
            session = s;
            s.PlayerJoined += p => game.NetPlayerJoined(p.Id, p.Name);
            s.PlayerLeft += p => game.NetPlayerLeft(p.Id);
            s.Disconnected += reason =>
            {
                game.NetLeave();
                Close();
                Lost?.Invoke(reason);
            };
        }

        private void Close()
        {
            answering?.Cancel();
            answering = null;
            session?.Dispose();
            session = null;
        }

        public void Dispose() => Leave();
    }
}
