using System.Buffers.Binary;
using System.Text;

namespace Thexder.Core.Game
{
    // Generated (procedural) levels: loading a LevelData instead of a level file, and the enemy reserve.
    internal sealed partial class ThexderGame
    {
        // The reserve brings an enemy in this many cells beyond the edge of the view, and parks it again when it is this
        // far beyond the edge (well out of sight, so nothing visibly appears or vanishes).
        private const int ReserveBringX = 24, ReserveBringY = 14;
        private const int ReserveParkX = 70, ReserveParkY = 44;
        private const int ReserveFreeSlots = 6; // Kept free for the enemies that spawn others (Bug Bombs, Mega Mines)

        private static readonly byte[] GeneratedMagic = "TXGN"u8.ToArray();

        /// <summary>The current level was built by a generator, not read from a level file.</summary>
        public bool LevelGenerated { get; private set; }

        /// <summary>Enemies of a generated level that are not in a slot now.</summary>
        public List<EnemyRecord> EnemyReserve { get; } = new();

        /// <summary>The generated level's name ("Terra Nova - Sector 3").</summary>
        public string GeneratedTitle { get; private set; } = "";

        /// <summary>The generated game being played; null for the classic campaign or a loaded level file.</summary>
        public IProceduralGame? Procedural { get; private set; }

        /// <summary>A generated game's record of the stats the player had when entering the current world.</summary>
        public WorldStart ProcWorldStart { get; } = new();

        /// <summary>
        /// Starts (or continues) a generated game: like the Game Select of the campaign, with the game's own levels,
        /// snapshots and progress file.
        /// </summary>
        public void StartProcedural(IProceduralGame game, bool resume)
        {
            bool isNew = game.IsNew;
            Procedural = game;
            SnapshotPrefix = game.SavePrefix;
            Difficulty = game.Difficulty;
            RealGame = true;
            LevelGfxReady = true;
            UpdateFormsForStage();
            if (!(resume && LoadSnapshot(continueSlot: true)))
            {
                World = game.World;
                Level = game.Level;
                LoadGenerated(game.BuildLevel(), fullReset: true);
                if (isNew)
                {
                    Player.Weapon = 1;
                    Player.Energy = 100;
                    Player.MaxEnergy = 100;
                    Inv.Count[ItemType.Laser] = 100;
                    SaveWorldStart();
                }
                else
                {
                    RestoreProcWorldStart();
                }
                SaveSnapshot(continueSlot: false);
            }
            WeaponCursor = Math.Max(0, Player.Weapon - 1);
            AltWeaponCursor = Math.Max(0, Player.AltWeapon - 1);
            HudFullRedraw = true;
            if (isNew && game.PlaysBriefings)
            {
                Player.PlayState = 5;
                PlayMovie($"2w{World}.avi");
            }
        }

        /// <summary>Leaves the generated game (back to the classic campaign rules and save files).</summary>
        public void EndProcedural()
        {
            Procedural = null;
            SnapshotPrefix = "";
            UpdateFormsForStage();
        }

        /// <summary>After logging in, or after a generated game: the player picks the game mode and it starts.</summary>
        public void ChooseMode()
        {
            var mode = host.ChooseGameMode();
            if (mode.Kind == GameModeKind.Procedural && mode.Game != null)
            {
                StartProcedural(mode.Game, mode.Resume);
                PauseCount = Math.Max(0, PauseCount - 1);
                return;
            }
            EndProcedural();
            if (mode.Kind == GameModeKind.Cancel)
            {
                RealGame = false;
                PauseCount = Math.Max(1, PauseCount); // The name screen, like after New Game
                TitleScreen();
                return;
            }
            if (GameSelect())
            {
                if (World == 1 && Level == 1)
                {
                    Player.PlayState = 5;
                    PlayMovie("2w1.avi");
                }
                PauseCount = Math.Max(0, PauseCount - 1);
                Sounds.Play(0x8d);
            }
        }

        /// <summary>The plane and the tank: from the second and third world of a generated campaign.</summary>
        private void UpdateFormsForStage()
        {
            int stage = Procedural?.Stage ?? 0;
            FormMinWorld[PlayerState.Plane] = Procedural == null ? 2 : stage >= 2 ? 1 : 99;
            FormMinWorld[PlayerState.Tank] = Procedural == null ? 3 : stage >= 3 ? 1 : 99;
        }

        private void RestoreProcWorldStart()
        {
            var ws = ProcWorldStart;
            if (ws.Energy == 0)
            {
                Player.Energy = 100;
                Player.MaxEnergy = 100;
                Inv.Count[ItemType.Laser] = 100;
                return;
            }
            Player.Weapon = ws.Weapon;
            Player.Energy = ws.Energy;
            Player.MaxEnergy = ws.MaxEnergy;
            Player.AltWeapon = ws.Weapon;
            Inv.CopyFrom(ws.Inventory);
        }

        /// <summary>The level being played again from the start: generated, or the level file.</summary>
        private void LoadCurrentLevel()
        {
            if (Procedural != null)
            {
                LoadGenerated(Procedural.BuildLevel(), fullReset: false);
            }
            else
            {
                LoadLevel(CurrentLevelFile.Length > 0 ? CurrentLevelFile : "W01L01.THL", fullReset: false);
            }
        }

        /// <summary>The level's name as the window titles show it.</summary>
        public string LevelLabel => LevelGenerated && GeneratedTitle.Length > 0 ? GeneratedTitle : $"{PlanetName(World)} - Level {Level}";

        /// <summary>LoadLevel for a generated level (same steps, data from memory).</summary>
        public void LoadGenerated(LevelData data, bool fullReset)
        {
            CurrentLevelFile = "";
            LevelGenerated = true;
            GeneratedTitle = data.Title;
            Header.Load(new byte[LevelHeader.Size]);
            Header.Version = LevelFileVersion;
            Header.StartX = data.StartX;
            Header.StartY = data.StartY;
            Header.ExitX = data.ExitX;
            Header.ExitY = data.ExitY;
            Header.TileSheet = data.TileSheet;
            Header.Background = data.Background;
            SetMapSize(data.Width, data.Height);
            data.Cells.AsSpan(0, Math.Min(data.Cells.Length, Map.ByteSize)).CopyTo(Map.Data);

            foreach (var t in Enemies)
            {
                t.Read(new byte[EnemyRecord.Size]);
            }
            EnemyReserve.Clear();
            // The enemies nearest the start take the slots; the others wait in the reserve.
            int sx = data.StartX, sy = data.StartY;
            int slot = 1;
            foreach (var t in data.Enemies.OrderBy(t => EnemyTypeIsBoss(t.Type) ? 0 : Math.Abs(t.X - sx) + 2 * Math.Abs(t.Y - sy)))
            {
                if (slot < EnemyCount - ReserveFreeSlots && (EnemyTypeIsBoss(t.Type) || Math.Abs(t.X - sx) < 3 * ViewWidthCells))
                {
                    Enemies[slot++].CopyFrom(t);
                }
                else
                {
                    var copy = new EnemyRecord();
                    copy.CopyFrom(t);
                    EnemyReserve.Add(copy);
                }
            }

            var record = new PlayerState();
            record.Read(data.PlayerRecord);
            if (fullReset)
            {
                Player.Read(data.PlayerRecord);
                Inv.Clear();
            }
            else
            {
                int oldForm = Player.Form;
                Player.Facing = record.Facing;
                Player.Form = record.Form;
                Player.LastDir = record.LastDir;
                if (Player.Form != oldForm)
                {
                    SetFormWeapons(Player.Form);
                }
            }

            for (int i = 0; i < Platforms.Length; i++)
            {
                if (i < data.Platforms.Count)
                {
                    Platforms[i].CopyFrom(data.Platforms[i]);
                }
                else
                {
                    Platforms[i].Read(new byte[EnemyRecord.Size]);
                }
            }
            for (int i = 0; i < Objects.Length; i++)
            {
                if (i < data.Objects.Count)
                {
                    Objects[i].CopyFrom(data.Objects[i]);
                }
                else
                {
                    Objects[i].Read(new byte[LevelObject.Size]);
                }
            }
            objectsMissing = false;

            PostLoad(fresh: true);
            if (LevelGfxReady && Header.SavedFlag == 0)
            {
                ApplyDifficulty();
            }
            host.StopMusic();
            host.StartLevelMusic(World, Level);
        }

        private const int LevelFileVersion = 3;

        private static bool EnemyTypeIsBoss(int type) => type < EnemyTypeTable.Count && EnemyTypeTable.IsBossType(type);

        /// <summary>ApplyDifficulty for the reserve: removed enemies, easy hit points, par and bosses.</summary>
        private void ApplyDifficultyToReserve(int mask, ref int par)
        {
            EnemyReserve.RemoveAll(t => t.State == 0 || (mask & t.DiffMask) != 0);
            foreach (var t in EnemyReserve)
            {
                if (Difficulty == 0)
                {
                    int q = t.Hp / 3;
                    t.Hp = q < 2 ? 1 : q;
                }
                if (t.Type != 1 && t.Type != 6)
                {
                    par += t.Hp * Header.PointsPerHit;
                }
                if (EnemyTypeIsBoss(t.Type))
                {
                    Header.BossCount++;
                    Header.ExitOpen = 0;
                }
            }
        }

        /// <summary>The types the reserve's enemies need are loaded with the level's.</summary>
        private void MarkReserveTypes()
        {
            foreach (var r in EnemyReserve)
            {
                if (r.State == EnemyRecord.Alive && r.Type < EnemyTypeTable.Count)
                {
                    enemyTypeUsed[r.Type] = true;
                }
            }
        }

        /// <summary>
        /// Every fourth frame of a generated level: brings reserve enemies near the player into free slots and parks the
        /// slots' enemies that are far away.
        /// </summary>
        private void UpdateEnemyReserve()
        {
            if (!LevelGenerated || NetActive || (FrameCounter & 3) != 0)
            {
                return;
            }
            int px = Player.X + FormW[Player.Form] / 2, py = Player.Y + FormH[Player.Form] / 2;
            int halfW = ViewWidthCells / 2, halfH = ViewHeightCells / 2;

            // Park: alive, not a boss, not attacking, no shot of its own in flight, far out of sight.
            for (int i = 1; i < EnemyCount; i++)
            {
                var r = Enemies[i];
                if (r.State == EnemyRecord.Free)
                {
                    continue;
                }
                int dx = Math.Abs(r.X + r.W / 2 - px), dy = Math.Abs(r.Y + r.H / 2 - py);
                if (dx <= halfW + ReserveParkX && dy <= halfH + ReserveParkY)
                {
                    continue;
                }
                if (r.State == EnemyRecord.Remains)
                {
                    r.State = EnemyRecord.Free;
                    continue;
                }
                if (r.State != EnemyRecord.Alive || TypeOf(r)?.IsBoss == true || r.Attacking != 0 || ShotOwnedBy(i))
                {
                    continue;
                }
                EnemyOccupancy(i, mark: false, useCurrent: true);
                var copy = new EnemyRecord();
                copy.CopyFrom(r);
                copy.Flags &= ~(EnemyRecord.FlagVisible | EnemyRecord.FlagHitFlash);
                EnemyReserve.Add(copy);
                r.State = EnemyRecord.Free;
                enemyNewX[i] = enemyNewY[i] = 0;
            }

            // Bring in: near, a free slot (keeping a few for spawned enemies), its box clear of the player and others.
            int free = 0;
            for (int i = 1; i < EnemyCount; i++)
            {
                if (Enemies[i].State == EnemyRecord.Free)
                {
                    free++;
                }
            }
            int brought = 0;
            for (int k = 0; k < EnemyReserve.Count && free > ReserveFreeSlots && brought < 6; k++)
            {
                var t = EnemyReserve[k];
                int dx = Math.Abs(t.X + t.W / 2 - px), dy = Math.Abs(t.Y + t.H / 2 - py);
                if (dx > halfW + ReserveBringX || dy > halfH + ReserveBringY || !ReserveSpotClear(t))
                {
                    continue;
                }
                int slot = 1;
                while (Enemies[slot].State != EnemyRecord.Free || ShotOwnedBy(slot))
                {
                    slot++;
                }
                if (slot >= EnemyCount)
                {
                    break;
                }
                Enemies[slot].CopyFrom(t);
                EnemyPrepare(Enemies[slot], fresh: true);
                enemyNewX[slot] = enemyNewY[slot] = 0;
                EnemyOccupancy(slot, mark: true, useCurrent: true);
                EnemyReserve.RemoveAt(k--);
                free--;
                brought++;
            }
        }

        private bool ShotOwnedBy(int enemy)
        {
            foreach (var s in Shots)
            {
                if (s.State != 0 && s.Owner == enemy)
                {
                    return true;
                }
            }
            return false;
        }

        private bool ReserveSpotClear(EnemyRecord t)
        {
            var p = Player;
            if (t.X < p.X + FormW[p.Form] && p.X < t.X + t.W && t.Y < p.Y + FormH[p.Form] && p.Y < t.Y + t.H)
            {
                return false;
            }
            for (int y = t.Y; y < t.Y + t.H; y++)
            {
                for (int x = t.X; x < t.X + t.W; x++)
                {
                    if (x < 0 || y < 0 || x >= MapW || y >= MapH || (Map.B2(Map.Index(x, y)) & 0x7f) != 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // ---- Saved games of generated levels -------------------------------------------------------------

        /// <summary>
        /// The block a snapshot of a generated level carries after the classic layout: title and reserve.
        /// </summary>
        private byte[] GeneratedSnapshotBlock()
        {
            byte[] title = Encoding.UTF8.GetBytes(GeneratedTitle);
            var block = new byte[4 + 2 + title.Length + 4 + EnemyReserve.Count * EnemyRecord.Size];
            var w = block.AsSpan();
            GeneratedMagic.CopyTo(w);
            BinaryPrimitives.WriteUInt16LittleEndian(w[4..], (ushort)title.Length);
            title.CopyTo(w[6..]);
            int pos = 6 + title.Length;
            BinaryPrimitives.WriteInt32LittleEndian(w[pos..], EnemyReserve.Count);
            pos += 4;
            foreach (var t in EnemyReserve)
            {
                t.Write(w.Slice(pos, EnemyRecord.Size));
                pos += EnemyRecord.Size;
            }
            return block;
        }

        /// <summary>Reads the generated-level block of a snapshot, if it has one.</summary>
        private bool ReadGeneratedSnapshotBlock(ReadOnlySpan<byte> r)
        {
            EnemyReserve.Clear();
            if (r.Length < 10 || !r[..4].SequenceEqual(GeneratedMagic))
            {
                LevelGenerated = false;
                GeneratedTitle = "";
                return false;
            }
            int len = BinaryPrimitives.ReadUInt16LittleEndian(r[4..]);
            GeneratedTitle = Encoding.UTF8.GetString(r.Slice(6, len));
            int pos = 6 + len;
            int count = BinaryPrimitives.ReadInt32LittleEndian(r[pos..]);
            pos += 4;
            for (int i = 0; i < count && pos + EnemyRecord.Size <= r.Length; i++, pos += EnemyRecord.Size)
            {
                var t = new EnemyRecord();
                t.Read(r.Slice(pos, EnemyRecord.Size));
                EnemyReserve.Add(t);
            }
            LevelGenerated = true;
            return true;
        }
    }
}
