using Thexder.Core.Assets;

namespace Thexder.Core.Game
{
    /// <summary>
    /// The connection a network game runs over: player 1 is the host; a message to player 0 goes to everybody else.
    /// </summary>
    internal interface INetLink
    {
        int LocalId { get; }

        void Send(int to, ReadOnlySpan<byte> data);

        bool TryReceive(out int from, out byte[] data);
    }

    /// <summary>The six parameters of a network game (the NETLOGON dialog, message 6).</summary>
    internal sealed record NetGameParameters(int GameNumber, int EnemyCount, int ScoreLimit, bool CreateEnemies, bool RegenEnemies, bool RegenObjects);

    /// <summary>One line of the Network Communicator's player list.</summary>
    internal sealed record NetScore(int Id, string Name, uint Score, int Colour, bool Local);

    // Network play: one host and up to nine clients on one generated level.
    internal sealed partial class ThexderGame
    {
        public const int NetHostId = 1;
        public const int NetSlots = 10;
        private const int NetEnemySlots = 30;
        private const int NetKickMs = 120_000;
        private const byte MsgSessionOver = 1, MsgClientState = 2, MsgWorldState = 3, MsgChat = 4, MsgRequestSetup = 5, MsgSetup = 6;

        /// <summary>Colour names by index: 0 is the host's own, untinted.</summary>
        public static readonly string[] NetColourNames =
            { "White", "Gold", "Light green", "Cyan", "Magenta", "Aqua", "Green", "Blue", "Violet", "Dark green" };

        private readonly NetRemote[] netRemotes = Enumerable.Range(0, NetSlots).Select(_ => new NetRemote()).ToArray();
        private readonly NetFire[] netFires = Enumerable.Range(0, 50).Select(_ => new NetFire()).ToArray();
        private readonly NetBomb[] netBombs = Enumerable.Range(0, 20).Select(_ => new NetBomb()).ToArray();
        private readonly long[] netEnemyDeathTick = new long[EnemyCount];
        private readonly bool[] netObjPresent = new bool[100];
        private readonly int[] netObjGuard = new int[100];
        private readonly EnemyRecord[] netEnemyPool = Enumerable.Range(0, 101).Select(_ => new EnemyRecord()).ToArray();
        private readonly LevelObject[] netObjPool = Enumerable.Range(0, 100).Select(_ => new LevelObject()).ToArray();
        private INetLink? netLink;
        private int netSeq;
        private int netLocalColour;
        private int netAimSlot = 9;

        public int NetMode { get; private set; }

        public bool NetActive => NetMode != 0;

        public NetGameParameters? NetParameters { get; private set; }

        public int NetLocalId => netLink?.LocalId ?? 0;

        /// <summary>The host ended the session, or the connection is gone; the argument says why.</summary>
        public event Action<string>? NetEnded;

        /// <summary>A client received the game's parameters and is now playing.</summary>
        public event Action? NetStarted;

        /// <summary>A chat line arrived (sender name, text).</summary>
        public event Action<string, string>? NetChatReceived;

        /// <summary>Somebody reached the score limit (the winner's name).</summary>
        public event Action<string>? NetGameWon;

        /// <summary>The player list or the scores may have changed (every 41 frames and on joins and leaves).</summary>
        public event Action? NetPlayersChanged;

        private sealed class NetRemote
        {
            public int Id;
            public string Name = "";
            public readonly PlayerState Rec = new();
            public uint Score;
            public int Colour;
            public int Seq;
            public long LastHeard;
            public bool HasPosition;

            public void Clear()
            {
                Id = 0;
                Name = "";
                Rec.Read(new byte[PlayerState.RecordSize]);
                Score = 0;
                Colour = 0;
                Seq = 0;
                HasPosition = false;
            }
        }

        private sealed class NetFire
        {
            public bool Used;
            public int Type, Shooter, Target, Tx, Ty, Owner;
        }

        private sealed class NetBomb
        {
            public bool Used;
            public int X, Y, Type, Owner;
        }

        /// <summary>
        /// Create (NETCONNECT 0x3f0): this machine hosts a game with these parameters and builds its level.
        /// </summary>
        public void NetHost(INetLink link, NetGameParameters parameters)
        {
            NetReset();
            netLink = link;
            NetParameters = parameters;
            NetStartGame(parameters.GameNumber);
            NetMode = 1;
        }

        /// <summary>Join (0x3f1): waits for the host's parameters (message 6); the local game holds meanwhile.</summary>
        public void NetJoin(INetLink link, IEnumerable<(int Id, string Name)> players)
        {
            NetReset();
            netLink = link;
            foreach (var (id, name) in players)
            {
                NetPlayerJoined(id, name);
            }
            NetMode = 3;
            PauseCount++;
            NetSend(NetHostId, MsgRequestSetup, _ => { });
        }

        /// <summary>A player joined the session (DirectPlay's ADDPLAYER).</summary>
        public void NetPlayerJoined(int id, string name)
        {
            if (netLink == null || id == netLink.LocalId || netRemotes.Any(r => r.Id == id))
            {
                return;
            }
            var slot = netRemotes.FirstOrDefault(r => r.Id == 0);
            if (slot != null)
            {
                slot.Clear();
                slot.Id = id;
                slot.Name = name.Length > 20 ? name[..20] : name;
                slot.LastHeard = Clock();
            }
            NetPlayersChanged?.Invoke();
        }

        /// <summary>A player left the session (DirectPlay's DELETEPLAYER).</summary>
        public void NetPlayerLeft(int id)
        {
            foreach (var r in netRemotes.Where(r => r.Id == id))
            {
                r.Clear();
            }
            NetPlayersChanged?.Invoke();
        }

        /// <summary>A host ends the session for everybody; the network state is dropped.</summary>
        public void NetLeave()
        {
            if (netLink == null)
            {
                return;
            }
            if (NetMode == 1)
            {
                NetSend(0, MsgSessionOver, _ => { });
            }
            NetReset();
        }

        private void NetReset()
        {
            if (Procedural != null)
            {
                // A network game interrupts a generated one: keep its place, then the campaign rules apply again
                if (SessionActive && Player.PlayState == 0)
                {
                    SaveSnapshot(continueSlot: true);
                }
                EndProcedural();
            }
            if (NetMode == 3)
            {
                PauseCount--;
            }
            NetMode = 0;
            netLink = null;
            netSeq = 0;
            netLocalColour = 0;
            foreach (var r in netRemotes)
            {
                r.Clear();
            }
            foreach (var f in netFires)
            {
                f.Used = false;
            }
            foreach (var b in netBombs)
            {
                b.Used = false;
            }
            Array.Clear(netEnemyDeathTick);
            Array.Clear(netObjPresent);
            Array.Clear(netObjGuard);
            for (int i = 0; i < Map.Data.Length / 4; i++)
            {
                if ((Map.B2(i) & 0x7f) == 0x7e)
                {
                    Map.SetB2(i, (byte)(Map.B2(i) & 0x80));
                }
            }
        }

        /// <summary>
        /// Called by the host application every frame, also while the game holds: a waiting client looks for the game's
        /// parameters here.
        /// </summary>
        public void NetPoll()
        {
            if (NetMode == 3)
            {
                NetReceive();
            }
        }

        /// <summary>The players in the session and their scores, best first.</summary>
        public IReadOnlyList<NetScore> NetScores()
        {
            var list = new List<NetScore>();
            if (netLink == null)
            {
                return list;
            }
            list.Add(new NetScore(netLink.LocalId, PlayerName, Player.Score, NetMode == 1 ? 0 : netLocalColour, true));
            for (int s = 0; s < NetSlots; s++)
            {
                var r = netRemotes[s];
                if (r.Id != 0)
                {
                    list.Add(new NetScore(r.Id, r.Name, r.Score, NetColourOf(s), false));
                }
            }
            return list.OrderByDescending(n => n.Score).ToList();
        }

        /// <summary>Diagnostics: the other players as this machine sees them.</summary>
        internal IReadOnlyList<(int Id, int X, int Y, uint Score)> NetRemotes() =>
            netRemotes.Where(r => r.Id != 0).Select(r => (r.Id, r.Rec.X, r.Rec.Y, r.Score)).ToList();

        /// <summary>The Network Communicator's Send (0x43e): a chat line to one player or to all (0).</summary>
        public void NetSendChat(int to, string text)
        {
            if (NetMode is 1 or 2 && !string.IsNullOrWhiteSpace(text))
            {
                string line = text.Length > 80 ? text[..80] : text;
                NetSend(to, MsgChat, w => w.Write(line));
            }
        }

        private int NetColourOf(int slot) => NetMode == 1 ? slot + 1 : netRemotes[slot].Colour;

        // ---- The network level ---------------------------------------------------------------------------

        /// <summary>
        /// Builds the level of a network game from <paramref name="gameNumber"/>: 16 of the 32 blocks of 40 x 34 cells in
        /// SEG01 / SEG02, enemies and objects picked from those blocks, a tile sheet and a background, all with the MSVC
        /// rand() seeded by the game number, so every player builds the same level.
        /// </summary>
        private void NetStartGame(int gameNumber)
        {
            Random.Reseed((uint)gameNumber);
            var seg = new[]
            {
                LevelFile.Load(Path.Combine(dataDirectory, "SEG01.THL")),
                LevelFile.Load(Path.Combine(dataDirectory, "SEG02.THL")),
            };
            var parameters = NetParameters!;
            Header.Load(seg[0].Header);
            SetMapSize(CellMap.ClassicWidth, CellMap.ClassicHeight); // The segments are laid out for 320 x 68
            LevelGenerated = false;
            EnemyReserve.Clear();
            foreach (var t in Enemies)
            {
                t.State = 0;
                t.Type = 6;
            }
            foreach (var o in Objects)
            {
                o.Kind = 0;
            }
            foreach (var p in Platforms)
            {
                p.Read(new byte[EnemyRecord.Size]);
            }
            foreach (var t in netEnemyPool)
            {
                t.Read(new byte[EnemyRecord.Size]);
            }
            foreach (var o in netObjPool)
            {
                o.Read(new byte[LevelObject.Size]);
            }

            var srcEnemy = new EnemyRecord[2][];
            var srcObj = new LevelObject[2][];
            var srcPlat = new EnemyRecord[2][];
            for (int f = 0; f < 2; f++)
            {
                srcEnemy[f] = Enumerable.Range(0, 100).Select(i => Record<EnemyRecord>(seg[f].Enemies, i, EnemyRecord.Size)).ToArray();
                srcObj[f] = Enumerable.Range(0, 100).Select(i => Record<LevelObject>(seg[f].Objects, i, LevelObject.Size)).ToArray();
                srcPlat[f] = Enumerable.Range(0, 30).Select(i => Record<EnemyRecord>(seg[f].Platforms, i, EnemyRecord.Size)).ToArray();
            }

            var used = new List<int>();
            int nPool = 1, nObjPool = 0, nPlat = 0;
            for (int d = 0; d < 16; d++)
            {
                int b;
                do
                {
                    b = Random.Next() & 31;
                }
                while (used.Contains(b));
                used.Add(b);
                int dstX = (d & 7) * 40, dstY = d < 8 ? 0 : 34;
                int file = b < 16 ? 0 : 1, s = b & 15;
                int srcX = (s & 7) * 40, srcY = s < 8 ? 0 : 34;
                for (int y = 0; y < 34; y++)
                {
                    Array.Copy(seg[file].Cells, ((srcY + y) * 320 + srcX) * 4, Map.Data, ((dstY + y) * 320 + dstX) * 4, 40 * 4);
                }
                bool InBlock(int x, int y) => (ushort)srcX <= (ushort)x && (ushort)x <= srcX + 39 && (ushort)srcY <= (ushort)y && (ushort)y <= srcY + 33;
                if (parameters.CreateEnemies)
                {
                    // The block test ignores the record state: empty records at (0, 0) take pool places too (literal).
                    for (int k = 1; k < 100 && nPool <= 99; k++)
                    {
                        var t = srcEnemy[file][k];
                        if (InBlock(t.X, t.Y))
                        {
                            netEnemyPool[nPool].CopyFrom(t);
                            netEnemyPool[nPool].X = (t.X + dstX - srcX) & 0xffff;
                            netEnemyPool[nPool].Y = (t.Y + dstY - srcY) & 0xffff;
                            nPool++;
                        }
                    }
                }
                for (int k = 0; k < 100 && nObjPool <= 99; k++)
                {
                    var o = srcObj[file][k];
                    if (InBlock(o.X, o.Y))
                    {
                        var copy = netObjPool[nObjPool++];
                        copy.CopyFrom(o);
                        copy.X = (o.X + dstX - srcX) & 0xffff;
                        copy.Y = (o.Y + dstY - srcY) & 0xffff;
                    }
                }
                for (int k = 0; k < 16 && nPlat < Platforms.Length; k++)
                {
                    var p = srcPlat[file][k];
                    if (InBlock(p.X, p.Y))
                    {
                        Platforms[nPlat].CopyFrom(p);
                        Platforms[nPlat].X = (p.X + dstX - srcX) & 0xffff;
                        Platforms[nPlat].Y = (p.Y + dstY - srcY) & 0xffff;
                        nPlat++;
                    }
                }
            }
            // The pick below can land one past the last candidate; with a full pool that is SEG01's platform record 0.
            if (nPool == 100)
            {
                netEnemyPool[100].CopyFrom(srcPlat[0][0]);
            }

            // Enemies: nEnemy - 1 random picks into slots 1...
            int nEnemy = Math.Min(parameters.EnemyCount, nPool);
            if (nEnemy > 29)
            {
                nEnemy = 30;
            }
            int selectable = Enumerable.Range(1, nPool).Count(k => netEnemyPool[k].State != 0);
            int dst = 1;
            for (int i = 1; i < nEnemy && dst - 1 < selectable; i++)
            {
                int k;
                do
                {
                    k = Random.Next() % nPool + 1;
                }
                while (netEnemyPool[k].State is 0 or 0x2a);
                Enemies[dst++].CopyFrom(netEnemyPool[k]);
                netEnemyPool[k].State = 0x2a;
            }
            int m = 0;
            for (int k = 1; k < nPool; k++)
            {
                if (netEnemyPool[k].State == 0x2a)
                {
                    netEnemyPool[k].State = 1;
                }
                else if (netEnemyPool[k].State != 0 && nEnemy + m < Enemies.Length)
                {
                    Enemies[nEnemy + m].CopyFrom(netEnemyPool[k]);
                    Enemies[nEnemy + m].State = 0;
                    m++;
                }
            }

            // Objects: up to 60 random picks (fix: no more than there are candidates).
            int nObj = Math.Min(nObjPool, 60);
            int objSelectable = Enumerable.Range(0, nObjPool).Count(k => netObjPool[k].Kind != 0);
            for (int i = 0; i < nObj && i < objSelectable; i++)
            {
                int k;
                do
                {
                    k = Random.Next() % nObjPool;
                }
                while (netObjPool[k].Kind is 0 or 0x2a);
                Objects[i].CopyFrom(netObjPool[k]);
                netObjPool[k].Kind = 0x2a;
            }
            m = 0;
            for (int k = 0; k < nObjPool; k++)
            {
                if (netObjPool[k].Kind == 0x2a)
                {
                    netObjPool[k].Kind = 1;
                }
                else if (netObjPool[k].Kind != 0 && nObj + m < Objects.Length)
                {
                    Objects[nObj + m].CopyFrom(netObjPool[k]);
                    Objects[nObj + m].Kind = 0;
                    m++;
                }
            }
            for (int i = 0; i < 100; i++)
            {
                netObjPresent[i] = Objects[i].Kind != 0;
            }

            Header.TileSheet = Random.Next() % 9;
            Header.Background = Random.Next() % 51;
            Header.World = 12;
            Header.SavedFlag = 0;
            World = 12;
            RealGame = false;
            Difficulty = 4;
            CurrentLevelFile = "";
            objectsMissing = false;
            SessionActive = true;
            LevelGfxReady = true;
            PostLoad(fresh: true);
            ApplyDifficulty();
            var player = Player;
            player.Form = PlayerState.Walker;
            player.Speed = FormSpeed[PlayerState.Walker];
            player.StateFlags = 0;
            Header.ExitOpen = 0;
            player.Facing = 2;
            player.Score = 0;
            player.Energy = 100;
            player.MaxEnergy = 100;
            HudFullRedraw = true;
            NetRespawn(instant: true);
        }

        private static T Record<T>(byte[] block, int index, int size) where T : new()
        {
            var record = new T();
            var span = block.AsSpan(index * size, size);
            switch (record)
            {
                case EnemyRecord t:
                    t.Read(span);
                    break;
                case LevelObject o:
                    o.Read(span);
                    break;
            }
            return record;
        }

        /// <summary>
        /// All items lost, a random spawn pad (one of the 16 block platforms), the tank gets thermal bombs and some
        /// laser, the others keep their laser ammunition (up to 100); the map is forgotten.
        /// </summary>
        private void NetRespawn(bool instant)
        {
            var p = Player;
            int oldLaser = Inv.Count[ItemType.Laser];
            Inv.Clear();
            Random.Reseed((uint)Clock());
            var pad = Platforms[Random.Next() & 15];
            if (!instant && TransportSprite(p.Form) != null)
            {
                if (p.Facing == 1)
                {
                    p.Facing = 2;
                    p.Frame = 0;
                }
                else
                {
                    p.Facing = 1;
                    p.Frame = 5;
                }
                p.StateFlags |= PlayerState.FlagTurning | PlayerState.FlagTransportOut;
                transportDestX = pad.X;
                transportDestY = pad.Y;
            }
            else
            {
                EraseMarks(p.X, p.Y, FormW[p.Form], FormH[p.Form]);
                p.X = pad.X;
                p.Y = pad.Y;
            }
            if (p.Form == PlayerState.Tank)
            {
                Inv.Count[ItemType.ThermalBomb] = 10;
                Inv.Count[ItemType.Laser] = 25;
                p.Weapon = ItemType.ThermalBomb;
            }
            else
            {
                Inv.Count[ItemType.Laser] = Math.Min(oldLaser, 100);
                p.Weapon = ItemType.Laser;
            }
            WeaponCursor = AltWeaponCursor = p.Weapon - 1;
            p.PlayState = 0;
            p.AltWeapon = 0;
            Sounds.Play(0x8d);
            for (int i = 0; i < Map.Count; i++)
            {
                Map.SetB1(i, (byte)(Map.B1(i) & 0x7f));
            }
            MapSurface.Clear();
            HudFullRedraw = true;
        }

        /// <summary>Esc in a network game: transport to a random pad for 10 points (energy is kept).</summary>
        public void NetEscape()
        {
            if (NetMode is 1 or 2)
            {
                Player.Score = Player.Score >= 10 ? Player.Score - 10 : 0;
                NetRespawn(instant: false);
            }
        }

        // ---- Every frame ---------------------------------------------------------------------------------

        /// <summary>Exchange, then the shared objects, bombs and shots of the other players.</summary>
        private void NetTick()
        {
            if (netLink == null || NetMode == 0)
            {
                return;
            }
            if (FrameCounter % 41 == 0)
            {
                NetPlayersChanged?.Invoke();
                NetCheckWinner();
            }
            if (NetMode == 1)
            {
                NetReceive();
                NetSendWorldState();
                NetCheckTimeouts();
            }
            else
            {
                NetSendClientState();
                NetReceive();
            }
            if (netLink == null)
            {
                return; // The session ended while receiving
            }

            for (int i = 0; i < 100; i++)
            {
                var o = Objects[i];
                if (netObjPresent[i])
                {
                    if (o.Kind == 0)
                    {
                        o.CopyFrom(netObjPool[i]);
                        int idx = Map.Index(o.X, o.Y);
                        if (idx >= 0 && idx < Map.Count)
                        {
                            Map.SetB3(idx, (byte)((Map.B3(idx) & 0xc0) | (o.Type & 0x3f)));
                            Map.SetB1(idx, (byte)((Map.B1(idx) & 0x80) | (o.Amount & 0x7f)));
                        }
                        o.Kind = LevelObject.KindPickup;
                    }
                }
                else if (o.Kind == LevelObject.KindPickup)
                {
                    o.Kind = 0;
                    int idx = Map.Index(o.X, o.Y);
                    if (idx >= 0 && idx < Map.Count)
                    {
                        Map.SetB3(idx, (byte)(Map.B3(idx) & 0xc0));
                    }
                }
            }
            foreach (var b in netBombs)
            {
                if (b.Used && b.Owner != netLink.LocalId)
                {
                    SpawnBomb(b.X, b.Y, b.Type);
                }
                b.Used = false;
            }
            foreach (var f in netFires)
            {
                if (f.Used && f.Owner != netLink.LocalId)
                {
                    ExecuteRemoteFire(f);
                }
                f.Used = false;
            }
        }

        // A fire event of another machine: from that player's muzzle (as seen here), or from an enemy shooting at that
        // player.
        private void ExecuteRemoteFire(NetFire f)
        {
            var slot = netRemotes.FirstOrDefault(r => r.Id == f.Owner && r.Id != 0);
            if (slot == null || f.Type < 0 || f.Type >= 0x29 || !ShotTypes[f.Type].Defined)
            {
                return;
            }
            var rec = slot.Rec;
            int camX = Math.Clamp(rec.X - (ViewWidthCells - 4) / 2, 0, MapW - ViewWidthCells);
            int camY = Math.Clamp(rec.Y - (ViewHeightCells - 6) / 2, 0, MapH - ViewHeightCells);
            int owner = f.Shooter == 0x7f ? 0x7e : f.Shooter; // A remote player's shot hits the local one
            ExecuteShot(rec, camX, camY, f.Type, f.Shooter, owner, f.Target, (short)(f.Tx * S), (short)(f.Ty * S), f.Owner);
        }

        // Queues a fire event of this machine for the others (FireEvent with NetMode != 0); false when the queue is
        // full.
        private bool NetQueueFire(int type, int shooter, int target, int x, int y)
        {
            var f = netFires.FirstOrDefault(e => !e.Used);
            if (f == null)
            {
                return false;
            }
            f.Used = true;
            f.Type = type;
            f.Shooter = shooter;
            f.Target = target;
            f.Tx = x / S;
            f.Ty = y / S;
            f.Owner = netLink?.LocalId ?? 0;
            return true;
        }

        // A bomb dropped by the local player, for the others (DropBomb in network play).
        private void NetQueueBomb(int x, int y, int type)
        {
            var b = netBombs.FirstOrDefault(e => !e.Used);
            if (b != null)
            {
                b.Used = true;
                b.X = x;
                b.Y = y;
                b.Type = type;
                b.Owner = netLink?.LocalId ?? 0;
            }
        }

        // ReadInput in network play: the other players' marks of the previous frame are cleared.
        private void NetClearRemoteMarks()
        {
            for (int i = 0; i < Map.Count; i++)
            {
                if ((Map.B2(i) & 0x7f) == 0x7e)
                {
                    Map.SetB2(i, (byte)(Map.B2(i) & 0x80));
                }
            }
        }

        // PlayerAnimate in network play: the other players occupy their boxes as 0x7E (fix: not before their first
        // position).
        private void NetStampRemoteMarks()
        {
            foreach (var r in netRemotes)
            {
                if (r.Id == 0 || !r.HasPosition)
                {
                    continue;
                }
                int form = FormOfSpriteSet(r.Rec.SpriteSet);
                for (int y = r.Rec.Y; y < r.Rec.Y + FormH[form]; y++)
                {
                    for (int x = r.Rec.X; x < r.Rec.X + FormW[form]; x++)
                    {
                        if (x < 0 || x >= MapW || y < 0 || y >= MapH)
                        {
                            continue;
                        }
                        int idx = y * MapW + x;
                        if ((Map.B2(idx) & 0x7f) == 0)
                        {
                            Map.SetB2(idx, (byte)((Map.B2(idx) & 0x80) | 0x7e));
                        }
                    }
                }
            }
        }

        private static int FormOfSpriteSet(int set) => set switch
        {
            1 or 4 or 0x1b or 0x1e or 0x21 or 0x24 or 0x2e => PlayerState.Plane,
            2 or 5 or 0x1c or 0x1f or 0x22 or 0x25 or 0x2f => PlayerState.Tank,
            _ => PlayerState.Walker,
        };

        // Every other player in its colour.
        private void DrawRemotePlayers()
        {
            for (int s = 0; s < NetSlots; s++)
            {
                var r = netRemotes[s];
                if (r.Id != 0 && r.HasPosition && (r.Rec.X != 0 || r.Rec.Y != 0))
                {
                    SceneTag(EntityRemote | s);
                    DrawPlayerSprite(r.Rec, Art.Tints.PlayerColour(NetColourOf(s)));
                    SceneTag(0);
                }
            }
        }

        // PlayerFire in network play: auto-aim tries the other players first, round robin.
        private bool NetAutoAim(int weapon, int scanL, int scanR, int camY, int viewH)
        {
            int start = netAimSlot;
            do
            {
                if (++netAimSlot > NetSlots - 1)
                {
                    netAimSlot = 0;
                }
                var r = netRemotes[netAimSlot];
                if (r.Id != 0 && r.HasPosition)
                {
                    int rx = r.Rec.X, ry = r.Rec.Y;
                    if (scanL < rx + 4 && camY < ry + 6 && rx < scanR && ry < camY + viewH)
                    {
                        int tx = Player.Facing != 1 ? rx + 4 : rx;
                        FireEvent(weapon, 0x7f, 0, tx * S, (ry + 3) * S);
                        return true;
                    }
                }
            }
            while (netAimSlot != start);
            if (--netAimSlot < 0)
            {
                netAimSlot = NetSlots - 1;
            }
            return false;
        }

        // PlayerPickups in network play: the object is gone for everybody; the host may put one back elsewhere.
        private void NetObjectTaken(int o)
        {
            if (NetMode == 1 && NetParameters?.RegenObjects == true)
            {
                int r = PickFreeObjectSlot();
                netObjPresent[r] = true;
                netObjGuard[r] = 8;
            }
            netObjPresent[o] = false;
        }

        private int PickFreeObjectSlot()
        {
            Random.Reseed((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            for (int n = 0; n < 50; n++)
            {
                int r = Random.Next() % 100;
                if (!netObjPresent[r])
                {
                    return r;
                }
            }
            int i = Array.IndexOf(netObjPresent, false);
            return i < 0 ? 99 : i;
        }

        // EnemyAnimateDrawAll in network play: an enemy killed elsewhere dies here too; a regenerated one comes back from
        // its template.
        private void NetEnemyUpdate(int i, EnemyRecord r, int x, int y, ref int dir)
        {
            if ((dir & 0x60) == 0)
            {
                if ((x != 0 || y != 0) && r.State == EnemyRecord.Remains)
                {
                    int k = 1;
                    for (int j = 1; j < 100; j++)
                    {
                        if (netEnemyPool[j].State == 1 && netEnemyPool[j].Type == r.Type)
                        {
                            k = j;
                            break;
                        }
                    }
                    r.CopyFrom(netEnemyPool[k]);
                    r.X = x;
                    r.Y = y;
                    r.State = EnemyRecord.Alive;
                }
            }
            else
            {
                if (r.State == EnemyRecord.Alive)
                {
                    EnemyDamage(i, r.Hp);
                }
                dir &= ~0x60;
            }
        }

        // EnemyDamage on the host: the time an enemy died, for regeneration.
        private void NetEnemyKilled(int i)
        {
            if (NetMode == 1 && i < NetEnemySlots)
            {
                netEnemyDeathTick[i] = Clock();
            }
        }

        // A template spot of the same enemy type (fix: x keeps all 9 bits).
        private void NetEnemyRegenPosition(int i)
        {
            Random.Reseed((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            int type = Enemies[i].Type;
            for (int n = 0; n < 50; n++)
            {
                int k = Random.Next() % 100 + 2;
                if (k < netEnemyPool.Length && netEnemyPool[k].State == 1 && netEnemyPool[k].Type == type)
                {
                    SetNetPos(i, netEnemyPool[k].X, netEnemyPool[k].Y, 0);
                    return;
                }
            }
            for (int k = 0; k < 100; k++)
            {
                if (netEnemyPool[k].State == 1 && netEnemyPool[k].Type == type)
                {
                    SetNetPos(i, netEnemyPool[k].X, netEnemyPool[k].Y, 0);
                    return;
                }
            }
        }

        private void SetNetPos(int i, int x, int y, int dir)
        {
            enemyNewX[i] = x & 0x1ff;
            enemyNewY[i] = y & 0x7f;
            enemyNewDir[i] = dir & 0xff;
        }

        private void NetCheckTimeouts()
        {
            long now = Clock();
            foreach (var r in netRemotes)
            {
                if (r.Id != 0 && now > r.LastHeard + NetKickMs)
                {
                    r.Clear(); // "Kill Player": the host stops listening to a player silent for two minutes
                    NetPlayersChanged?.Invoke();
                }
            }
        }

        // The score limit (NetCommRefresh): the first machine that sees a score at the limit ends the game.
        private void NetCheckWinner()
        {
            int limit = NetParameters?.ScoreLimit ?? 0;
            if (limit <= 0)
            {
                return;
            }
            var best = netRemotes.Where(r => r.Id != 0).OrderByDescending(r => r.Score).FirstOrDefault();
            string? winner = best != null && best.Score >= limit ? best.Name : Player.Score >= limit ? PlayerName : null;
            if (winner != null)
            {
                NetGameWon?.Invoke(winner);
            }
        }

        private void NetSend(int to, byte type, Action<BinaryWriter> body)
        {
            if (netLink == null)
            {
                return;
            }
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(type);
                w.Write((ushort)netSeq);
                body(w);
            }
            netSeq = netSeq > 30000 ? 0 : netSeq + 1;
            netLink.Send(to, ms.ToArray());
        }

        // Message 2 (client -> host): position, animation, score, this frame's shots and bombs, the enemies this machine
        // moved and the objects it still has.
        private void NetSendClientState()
        {
            var p = Player;
            NetSend(NetHostId, MsgClientState, w =>
            {
                w.Write((ushort)p.X);
                w.Write((byte)p.Y);
                w.Write((byte)p.SpriteSet);
                w.Write((byte)p.Frame);
                w.Write(p.Score);
                WriteFires(w);
                WriteNetPos(w);
                WriteObjects(w);
                WriteBombs(w);
            });
        }

        // Message 3 (host -> all): every player, the merged shots, enemies, objects and bombs.
        private void NetSendWorldState()
        {
            var p = Player;
            long now = Clock();
            for (int i = 0; i < NetEnemySlots; i++)
            {
                if (NetParameters?.RegenEnemies == true && netEnemyDeathTick[i] > 10 && now > netEnemyDeathTick[i] + 10_000)
                {
                    NetEnemyRegenPosition(i);
                    netEnemyDeathTick[i] = 8;
                }
                else if (netEnemyDeathTick[i] != 0 && netEnemyDeathTick[i] < 10)
                {
                    netEnemyDeathTick[i]--;
                }
            }
            for (int i = 0; i < 100; i++)
            {
                if (netObjGuard[i] != 0 && netObjGuard[i] < 10)
                {
                    netObjGuard[i]--;
                }
            }
            NetSend(0, MsgWorldState, w =>
            {
                var players = new List<(int Id, int X, int Y, uint Score, int Frame, int Set, int Colour)>
                {
                    (netLink!.LocalId, p.X, p.Y, p.Score, p.Frame, p.SpriteSet, 0),
                };
                for (int s = 0; s < NetSlots; s++)
                {
                    var r = netRemotes[s];
                    if (r.Id != 0)
                    {
                        players.Add((r.Id, r.Rec.X, r.Rec.Y, r.Score, r.Rec.Frame, r.Rec.SpriteSet, s + 1));
                    }
                }
                w.Write((byte)players.Count);
                foreach (var e in players)
                {
                    w.Write((byte)e.Id);
                    w.Write((ushort)e.X);
                    w.Write((byte)e.Y);
                    w.Write(e.Score);
                    w.Write((byte)e.Frame);
                    w.Write((byte)e.Set);
                    w.Write((byte)e.Colour);
                }
                WriteFires(w);
                WriteNetPos(w);
                WriteObjects(w);
                WriteBombs(w);
            });
        }

        private void WriteFires(BinaryWriter w)
        {
            var fires = netFires.Where(f => f.Used).ToList();
            w.Write((byte)fires.Count);
            foreach (var f in fires)
            {
                w.Write((byte)f.Owner);
                w.Write((byte)f.Type);
                w.Write((byte)f.Shooter);
                w.Write((byte)f.Target);
                w.Write((short)f.Tx);
                w.Write((short)f.Ty);
            }
        }

        private void WriteNetPos(BinaryWriter w)
        {
            for (int i = 0; i < NetEnemySlots; i++)
            {
                w.Write((ushort)enemyNewX[i]);
                w.Write((byte)enemyNewY[i]);
                w.Write((byte)enemyNewDir[i]);
            }
        }

        private void WriteObjects(BinaryWriter w)
        {
            var bits = new byte[13];
            for (int i = 0; i < 100; i++)
            {
                if (netObjPresent[i])
                {
                    bits[i >> 3] |= (byte)(1 << (i & 7));
                }
            }
            w.Write(bits);
        }

        private void WriteBombs(BinaryWriter w)
        {
            var bombs = netBombs.Where(b => b.Used).ToList();
            w.Write((byte)bombs.Count);
            foreach (var b in bombs)
            {
                w.Write((byte)b.Owner);
                w.Write((ushort)b.X);
                w.Write((byte)b.Y);
                w.Write((byte)b.Type);
            }
        }

        // Appends received fire events into free queue slots.
        private void ReadFires(BinaryReader r, int ownerOverride)
        {
            int n = r.ReadByte();
            for (int k = 0; k < n; k++)
            {
                int owner = r.ReadByte();
                int type = r.ReadByte(), shooter = r.ReadByte(), target = r.ReadByte();
                int tx = r.ReadInt16(), ty = r.ReadInt16();
                var f = netFires.FirstOrDefault(e => !e.Used);
                if (f == null)
                {
                    continue;
                }
                f.Used = true;
                f.Owner = ownerOverride != 0 ? ownerOverride : owner;
                f.Type = type;
                f.Shooter = shooter;
                f.Target = target;
                f.Tx = tx;
                f.Ty = ty;
            }
        }

        // Received bombs keep their owner, so each machine spawns every other player's bomb exactly once (fix).
        private void ReadBombs(BinaryReader r, int ownerOverride)
        {
            int n = r.ReadByte();
            for (int k = 0; k < n; k++)
            {
                int owner = r.ReadByte();
                int x = r.ReadUInt16(), y = r.ReadByte(), type = r.ReadByte();
                var b = netBombs.FirstOrDefault(e => !e.Used);
                if (b == null)
                {
                    continue;
                }
                b.Used = true;
                b.Owner = ownerOverride != 0 ? ownerOverride : owner;
                b.X = x;
                b.Y = y;
                b.Type = type;
            }
        }

        private static (int X, int Y, int Dir)[] ReadNetPos(BinaryReader r)
        {
            var list = new (int, int, int)[NetEnemySlots];
            for (int i = 0; i < NetEnemySlots; i++)
            {
                list[i] = (r.ReadUInt16(), r.ReadByte(), r.ReadByte());
            }
            return list;
        }

        private static bool[] ReadObjects(BinaryReader r)
        {
            byte[] bits = r.ReadBytes(13);
            var present = new bool[100];
            for (int i = 0; i < 100; i++)
            {
                present[i] = (bits[i >> 3] & (1 << (i & 7))) != 0;
            }
            return present;
        }

        /// <summary>Every queued message.</summary>
        private void NetReceive()
        {
            while (netLink != null && netLink.TryReceive(out int from, out byte[] data))
            {
                if (data.Length < 3)
                {
                    continue;
                }
                var remote = netRemotes.FirstOrDefault(r => r.Id == from && r.Id != 0);
                int seq = data[1] | (data[2] << 8);
                if (remote != null)
                {
                    bool stale = seq != 0 && remote.Seq >= seq;
                    remote.Seq = seq;
                    remote.LastHeard = Clock();
                    if (stale)
                    {
                        continue; // "Sync Error": out of order
                    }
                }
                using var r = new BinaryReader(new MemoryStream(data, 3, data.Length - 3));
                try
                {
                    switch (data[0])
                    {
                        case MsgSessionOver:
                            NetReset();
                            NetEnded?.Invoke("The host ended the network game.");
                            return;
                        case MsgClientState:
                            if (NetMode == 1)
                            {
                                OnClientState(from, remote, r);
                            }
                            break;
                        case MsgWorldState:
                            if (NetMode == 2)
                            {
                                OnWorldState(r);
                            }
                            break;
                        case MsgChat:
                            NetChatReceived?.Invoke(remote?.Name ?? $"Player {from}", r.ReadString());
                            break;
                        case MsgRequestSetup:
                            if (NetMode == 1 && NetParameters is { } p)
                            {
                                NetSend(0, MsgSetup, w =>
                                {
                                    w.Write(p.GameNumber);
                                    w.Write(p.EnemyCount);
                                    w.Write(p.ScoreLimit);
                                    w.Write(p.CreateEnemies);
                                    w.Write(p.RegenEnemies);
                                    w.Write(p.RegenObjects);
                                });
                            }
                            break;
                        case MsgSetup:
                            if (NetMode == 3)
                            {
                                NetParameters = new NetGameParameters(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadBoolean(), r.ReadBoolean(), r.ReadBoolean());
                                NetStartGame(NetParameters.GameNumber);
                                NetMode = 2;
                                PauseCount--;
                                NetStarted?.Invoke();
                                return; // The rest belongs to the game that starts now
                            }
                            break;
                    }
                }
                catch (EndOfStreamException)
                {
                    // A truncated message is ignored
                }
            }
        }

        // OnClientState (host): the client's position and score, its enemy reports merged into the host's view, its
        // objects, its shots and bombs appended for everybody.
        private void OnClientState(int from, NetRemote? remote, BinaryReader r)
        {
            int x = r.ReadUInt16(), y = r.ReadByte(), set = r.ReadByte(), frame = r.ReadByte();
            uint score = r.ReadUInt32();
            if (remote != null)
            {
                remote.Score = score;
                remote.Rec.X = x;
                remote.Rec.Y = y;
                remote.Rec.SpriteSet = set;
                remote.Rec.Frame = frame;
                remote.HasPosition = true;
            }
            ReadFires(r, from);
            var pos = ReadNetPos(r);
            for (int i = 0; i < NetEnemySlots; i++)
            {
                if ((enemyNewDir[i] & 0x60) != 0)
                {
                    continue; // The host already has a death report
                }
                var (ex, ey, ed) = pos[i];
                if ((ed & 0x60) == 0)
                {
                    if (enemyNewX[i] == 0 && enemyNewY[i] == 0 && (ex != 0 || ey != 0))
                    {
                        SetNetPos(i, ex, ey, ed); // Nobody here moved it: take the client's
                    }
                }
                else if (netEnemyDeathTick[i] == 0)
                {
                    SetNetPos(i, ex, ey, ed);
                    netEnemyDeathTick[i] = Clock();
                }
            }
            var present = ReadObjects(r);
            if (NetParameters?.RegenObjects == true)
            {
                for (int i = 0; i < 100; i++)
                {
                    if (netObjPresent[i] && !present[i] && netObjGuard[i] == 0)
                    {
                        int k = PickFreeObjectSlot();
                        netObjPresent[k] = true;
                        netObjGuard[k] = 8;
                        netObjPresent[i] = false;
                    }
                }
            }
            else
            {
                for (int i = 0; i < 100; i++)
                {
                    if (!present[i])
                    {
                        netObjPresent[i] = false;
                    }
                }
            }
            ReadBombs(r, from);
        }

        // OnWorldState (clients): everybody's position, score and colour, the host's enemies and objects, and the shots
        // and bombs of the other players.
        private void OnWorldState(BinaryReader r)
        {
            int n = r.ReadByte();
            for (int k = 0; k < n; k++)
            {
                int id = r.ReadByte(), x = r.ReadUInt16(), y = r.ReadByte();
                uint score = r.ReadUInt32();
                int frame = r.ReadByte(), set = r.ReadByte(), colour = r.ReadByte();
                if (id == netLink!.LocalId)
                {
                    netLocalColour = colour;
                    continue;
                }
                var remote = netRemotes.FirstOrDefault(e => e.Id == id && e.Id != 0);
                if (remote != null)
                {
                    remote.Rec.X = x;
                    remote.Rec.Y = y;
                    remote.Rec.Frame = frame;
                    remote.Rec.SpriteSet = set;
                    remote.Score = score;
                    remote.Colour = colour;
                    remote.HasPosition = true;
                }
            }
            ReadFires(r, 0);
            var pos = ReadNetPos(r);
            for (int i = 0; i < NetEnemySlots; i++)
            {
                SetNetPos(i, pos[i].X, pos[i].Y, pos[i].Dir);
            }
            var present = ReadObjects(r);
            Array.Copy(present, netObjPresent, 100);
            ReadBombs(r, 0);
        }
    }
}
