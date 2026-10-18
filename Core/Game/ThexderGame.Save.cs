using System.Buffers.Binary;
using System.Text;
using Serilog;

namespace Thexder.Core.Game
{
    /// <summary>A world-start record (.thw): the stats the player had when entering a world, per difficulty.</summary>
    internal sealed class WorldStart
    {
        public int Weapon;
        public int Energy;
        public int MaxEnergy;
        public readonly Inventory Inventory = new();
    }

    /// <summary>One level of the Hall of Fame (0x70-byte record).</summary>
    internal sealed class HallOfFameEntry
    {
        public string LevelName = "";
        public uint LevelScore;
        public string CumulativeName = "";
        public uint CumulativeScore;
    }

    // They live in the save directory and are named after the player.
    internal sealed partial class ThexderGame
    {
        public const string HallOfFameFile = "Thexder~Hall~Of~Fame.ths";
        public const int LevelCount = 50;
        private const int SnapshotSize = 0x185f0;
        private const int WorldStartSize = 0x10e;
        private const int HofRecordSize = 0x70;

        /// <summary>Where the save files go.</summary>
        public string SaveDirectory { get; set; } = "";

        public string PlayerName { get; set; } = "";

        public int LastDifficulty { get; set; }

        public (int World, int Level)[] Progress { get; } = Enumerable.Repeat((1, 1), 5).ToArray();

        public int[] KeyBindings { get; } = (int[])DefaultKeyBindings.Clone();

        public static readonly int[] DefaultKeyBindings = { 5, 49, 48, 51, 9, 3, 59, 61, 58, 60, 57, 54, 56, 55, 40, 22, 38, 44, 26 };

        public WorldStart[] WorldStarts { get; } = Enumerable.Range(0, LevelCount).Select(_ => new WorldStart()).ToArray();

        public (uint Score, uint Par)[] LevelScores { get; } = new (uint, uint)[LevelCount];

        private string PlayerFile(string extension) => Path.Combine(SaveDirectory, PlayerName + extension);

        /// <summary>The continue (.th#) or level-start (.tr#) snapshot of the game being played.</summary>
        private string SnapshotFile(bool continueSlot) =>
            SnapshotPrefix.Length > 0
                ? Path.Combine(SaveDirectory, $"{PlayerName}~{SnapshotPrefix}{(continueSlot ? ".thc" : ".thr")}")
                : PlayerFile((continueSlot ? ".th" : ".tr") + Difficulty);

        /// <summary>Set while a procedural game is played: the name its snapshot files carry (e.g. "arc").</summary>
        public string SnapshotPrefix { get; set; } = "";

        // ---- Level snapshots (.th# continue, .tr# replay) -------------------------------------------------

        /// <summary>The whole game state; continueSlot writes .th#, otherwise .tr#.</summary>
        public void SaveSnapshot(bool continueSlot)
        {
            Header.Version = 1;
            Header.SavedFlag = 1;
            Header.World = World;
            Header.Level = Level;
            byte[] data = new byte[SnapshotSize - CellMap.ClassicByteSize + Map.ByteSize];
            var w = data.AsSpan();
            int pos = 0;
            Header.Data.CopyTo(w);
            pos += LevelHeader.Size;
            BinaryPrimitives.WriteUInt16LittleEndian(w[pos..], (ushort)MapW);
            BinaryPrimitives.WriteUInt16LittleEndian(w[(pos + 2)..], (ushort)MapH);
            pos += 4;
            Map.Data.CopyTo(w[pos..]);
            pos += Map.ByteSize;
            BinaryPrimitives.WriteUInt16LittleEndian(w[pos..], 100);
            pos += 2;
            EnemyRecord.WriteArray(Enemies, w.Slice(pos, 100 * EnemyRecord.Size));
            pos += 100 * EnemyRecord.Size;
            Player.Write(w.Slice(pos, PlayerState.RecordSize));
            pos += PlayerState.RecordSize;
            Inv.Write(w.Slice(pos, Inventory.ByteSize));
            pos += Inventory.ByteSize;
            EnemyRecord.WriteArray(Platforms, w.Slice(pos, Platforms.Length * EnemyRecord.Size));
            pos += Platforms.Length * EnemyRecord.Size;
            for (int i = 0; i < Objects.Length; i++, pos += LevelObject.Size)
            {
                Objects[i].Write(w.Slice(pos, LevelObject.Size));
            }
            for (int i = 0; i < Shots.Length; i++, pos += ShotRecord.Size)
            {
                Shots[i].Write(w.Slice(pos, ShotRecord.Size));
            }
            for (int i = 0; i < Timed.Length; i++, pos += TimedObject.Size)
            {
                Timed[i].Write(w.Slice(pos, TimedObject.Size));
            }
            if (LevelGenerated)
            {
                data = data.Concat(GeneratedSnapshotBlock()).ToArray();
            }
            WriteFile(SnapshotFile(continueSlot), data);
            if (SnapshotPrefix.Length == 0)
            {
                Progress[Difficulty] = (World, Level); // The campaign's Game Select slots
                SaveThg();
            }
            Procedural?.Save(this);
        }

        /// <summary>Restores a snapshot; false when there is none.</summary>
        public bool LoadSnapshot(bool continueSlot)
        {
            byte[]? data = ReadFile(SnapshotFile(continueSlot));
            if (data == null || data.Length < LevelHeader.Size + 4)
            {
                return false;
            }
            var r = data.AsSpan();
            int width = BinaryPrimitives.ReadUInt16LittleEndian(r[LevelHeader.Size..]);
            int height = BinaryPrimitives.ReadUInt16LittleEndian(r[(LevelHeader.Size + 2)..]);
            if (width == 0 || height == 0 || data.Length < SnapshotSize - CellMap.ClassicByteSize + width * height * 4)
            {
                return false;
            }
            int pos = 0;
            Header.Load(r);
            World = Header.World;
            Level = Header.Level;
            CurrentLevelFile = $"W{World:00}L{Level:00}.THL";
            pos += LevelHeader.Size + 4;
            SetMapSize(width, height);
            Map.Load(r.Slice(pos, Map.ByteSize));
            pos += Map.ByteSize + 2;
            for (int i = 0; i < Enemies.Length; i++, pos += EnemyRecord.Size)
            {
                Enemies[i].Read(r.Slice(pos, EnemyRecord.Size));
            }
            Player.Read(r.Slice(pos, PlayerState.RecordSize));
            pos += PlayerState.RecordSize;
            Inv.Read(r.Slice(pos, Inventory.ByteSize));
            pos += Inventory.ByteSize;
            for (int i = 0; i < Platforms.Length; i++, pos += EnemyRecord.Size)
            {
                Platforms[i].Read(r.Slice(pos, EnemyRecord.Size));
            }
            for (int i = 0; i < Objects.Length; i++, pos += LevelObject.Size)
            {
                Objects[i].Read(r.Slice(pos, LevelObject.Size));
            }
            for (int i = 0; i < Shots.Length; i++, pos += ShotRecord.Size)
            {
                Shots[i].Read(r.Slice(pos, ShotRecord.Size));
            }
            for (int i = 0; i < Timed.Length; i++, pos += TimedObject.Size)
            {
                Timed[i].Read(r.Slice(pos, TimedObject.Size));
            }
            objectsMissing = false;
            if (ReadGeneratedSnapshotBlock(r[pos..]))
            {
                CurrentLevelFile = "";
            }
            PostLoad(fresh: false);
            RebuildMapFromSeen();
            host.StopMusic();
            host.StartLevelMusic(World, Level);
            return true;
        }

        // ---- Per-player progress (.thg) and world starts (.thw) ------------------------------------------

        public void SaveThg()
        {
            byte[] data = new byte[0x18 + 0x4c];
            BinaryPrimitives.WriteUInt16LittleEndian(data, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)LastDifficulty);
            for (int d = 0; d < 5; d++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4 + d * 4), (ushort)Progress[d].World);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6 + d * 4), (ushort)Progress[d].Level);
            }
            for (int k = 0; k < KeyBindings.Length; k++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x18 + k * 4), (uint)KeyBindings[k]);
            }
            WriteFile(PlayerFile(".thg"), data);
        }

        /// <summary>Missing file = difficulty 0, every slot at W1 L1, bindings unchanged.</summary>
        public void LoadThg()
        {
            LastDifficulty = 0;
            for (int d = 0; d < 5; d++)
            {
                Progress[d] = (1, 1);
            }
            byte[]? data = ReadFile(PlayerFile(".thg"));
            if (data == null || data.Length < 0x18 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 1)
            {
                return;
            }
            LastDifficulty = Math.Clamp((int)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)), 0, 4);
            for (int d = 0; d < 5; d++)
            {
                Progress[d] = (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4 + d * 4)), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6 + d * 4)));
            }
            if (data.Length >= 0x18 + 0x4c)
            {
                for (int k = 0; k < KeyBindings.Length; k++)
                {
                    int index = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x18 + k * 4));
                    if (index is >= 0 and < 62)
                    {
                        KeyBindings[k] = index;
                    }
                }
            }
        }

        /// <summary>Records the current stats for this world and difficulty.</summary>
        public void SaveWorldStart()
        {
            if (Procedural != null)
            {
                ProcWorldStart.Weapon = Player.Weapon;
                ProcWorldStart.Energy = Player.Energy;
                ProcWorldStart.MaxEnergy = Player.MaxEnergy;
                ProcWorldStart.Inventory.CopyFrom(Inv);
                Procedural.Save(this);
                return;
            }
            int i = World * 5 + Difficulty - 5;
            if (i < 0 || i >= WorldStarts.Length)
            {
                return;
            }
            var ws = WorldStarts[i];
            ws.Weapon = Player.Weapon;
            ws.Energy = Player.Energy;
            ws.MaxEnergy = Player.MaxEnergy;
            ws.Inventory.CopyFrom(Inv);
            byte[] data = new byte[2 + LevelCount * WorldStartSize];
            BinaryPrimitives.WriteUInt16LittleEndian(data, 1);
            for (int k = 0; k < LevelCount; k++)
            {
                var span = data.AsSpan(2 + k * WorldStartSize, WorldStartSize);
                var rec = WorldStarts[k];
                BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)rec.Weapon);
                BinaryPrimitives.WriteUInt16LittleEndian(span[2..], (ushort)rec.Energy);
                BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)rec.MaxEnergy);
                rec.Inventory.Write(span[6..]);
            }
            WriteFile(PlayerFile(".thw"), data);
        }

        /// <summary>Missing file = every record empty.</summary>
        public void LoadThw()
        {
            foreach (var ws in WorldStarts)
            {
                ws.Weapon = ws.Energy = ws.MaxEnergy = 0;
                ws.Inventory.Clear();
            }
            byte[]? data = ReadFile(PlayerFile(".thw"));
            if (data == null || data.Length < 2 + LevelCount * WorldStartSize || BinaryPrimitives.ReadUInt16LittleEndian(data) != 1)
            {
                return;
            }
            for (int k = 0; k < LevelCount; k++)
            {
                var span = data.AsSpan(2 + k * WorldStartSize, WorldStartSize);
                var rec = WorldStarts[k];
                rec.Weapon = BinaryPrimitives.ReadUInt16LittleEndian(span);
                rec.Energy = BinaryPrimitives.ReadUInt16LittleEndian(span[2..]);
                rec.MaxEnergy = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
                rec.Inventory.Read(span[6..]);
            }
        }

        // ---- Scores (.ths) and the Hall of Fame ----------------------------------------------------------

        public void LoadLevelScores()
        {
            Array.Clear(LevelScores);
            byte[]? data = ReadFile(PlayerFile(".ths"));
            if (data == null || data.Length < 2 + LevelCount * 8 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 1)
            {
                return;
            }
            for (int i = 0; i < LevelCount; i++)
            {
                LevelScores[i] = (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2 + i * 8)), BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(6 + i * 8)));
            }
        }

        /// <summary>SaveLevelScore + UpdateHof: after Continue on the score screen.</summary>
        public void SaveLevelScore()
        {
            int idx = (World - 1) * 5 + (Level - 1);
            if (idx < 0 || idx >= LevelCount)
            {
                return;
            }
            LoadLevelScores();
            if (Player.Score > LevelScores[idx].Score)
            {
                LevelScores[idx] = (Player.Score, (uint)Header.Par);
            }
            byte[] data = new byte[2 + LevelCount * 8];
            BinaryPrimitives.WriteUInt16LittleEndian(data, 1);
            for (int i = 0; i < LevelCount; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2 + i * 8), LevelScores[i].Score);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6 + i * 8), LevelScores[i].Par);
            }
            WriteFile(PlayerFile(".ths"), data);

            var hof = LoadHallOfFame();
            if (LevelScores[idx].Score > hof[idx].LevelScore)
            {
                hof[idx].LevelName = PlayerName;
                hof[idx].LevelScore = LevelScores[idx].Score;
            }
            var (cumulative, _) = Cumulative(idx);
            if (cumulative > hof[idx].CumulativeScore)
            {
                hof[idx].CumulativeName = PlayerName;
                hof[idx].CumulativeScore = cumulative;
            }
            SaveHallOfFame(hof);
        }

        public (uint Score, uint Par) Cumulative(int idx)
        {
            uint score = 0, par = 0;
            for (int k = 0; k <= idx && k < LevelCount; k++)
            {
                score += LevelScores[k].Score;
                par += LevelScores[k].Par;
            }
            return (score, par);
        }

        /// <summary>The Hall of Fame a new installation starts with.</summary>
        private static HallOfFameEntry[] StockHallOfFame()
        {
            var hof = Enumerable.Range(0, LevelCount).Select(_ => new HallOfFameEntry()).ToArray();
            void Default(int i, string name, uint level, uint cumulative)
            {
                hof[i].LevelName = name;
                hof[i].LevelScore = level;
                hof[i].CumulativeName = name;
                hof[i].CumulativeScore = cumulative;
            }
            Default(29, "Phantom", 33600, 1908500);
            Default(34, "TCS", 39900, 2300500);
            Default(39, "Jaxon", 43500, 2500000);
            Default(44, "Bonzi", 45300, 2800000);
            Default(49, "Camelot", 250000, 4526000);
            return hof;
        }

        public HallOfFameEntry[] LoadHallOfFame()
        {
            var hof = Enumerable.Range(0, LevelCount).Select(_ => new HallOfFameEntry()).ToArray();
            byte[]? data = ReadFile(Path.Combine(SaveDirectory, HallOfFameFile));
            if (data == null || data.Length < 2 + LevelCount * HofRecordSize || BinaryPrimitives.ReadUInt16LittleEndian(data) != 1)
            {
                return StockHallOfFame();
            }
            for (int i = 0; i < LevelCount; i++)
            {
                var span = data.AsSpan(2 + i * HofRecordSize, HofRecordSize);
                hof[i].LevelName = CString(span[..52]);
                hof[i].LevelScore = BinaryPrimitives.ReadUInt32LittleEndian(span[52..]);
                hof[i].CumulativeName = CString(span.Slice(56, 52));
                hof[i].CumulativeScore = BinaryPrimitives.ReadUInt32LittleEndian(span[108..]);
            }
            return hof;
        }

        internal void SaveHallOfFame(HallOfFameEntry[] hof)
        {
            byte[] data = new byte[2 + LevelCount * HofRecordSize];
            BinaryPrimitives.WriteUInt16LittleEndian(data, 1);
            for (int i = 0; i < LevelCount; i++)
            {
                var span = data.AsSpan(2 + i * HofRecordSize, HofRecordSize);
                WriteCString(span[..52], hof[i].LevelName);
                BinaryPrimitives.WriteUInt32LittleEndian(span[52..], hof[i].LevelScore);
                WriteCString(span.Slice(56, 52), hof[i].CumulativeName);
                BinaryPrimitives.WriteUInt32LittleEndian(span[108..], hof[i].CumulativeScore);
            }
            WriteFile(Path.Combine(SaveDirectory, HallOfFameFile), data);
        }

        /// <summary>The players who have a progress file (the title screen's name list).</summary>
        /// <summary>A profile's last classic difficulty and where that game stands, from their .thg (null without one).</summary>
        public (int Difficulty, int World, int Level)? ProfileProgress(string name)
        {
            string path = Path.Combine(SaveDirectory, name + ".thg");
            byte[]? data = File.Exists(path) ? ReadFile(path) : null;
            if (data == null || data.Length < 0x18 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 1)
            {
                return null;
            }
            int d = Math.Clamp((int)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)), 0, 4);
            return (d, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4 + d * 4)), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6 + d * 4)));
        }

        /// <summary>
        /// Removes a profile: every file it saved (classic and generated games, progress, world starts, scores) and its
        /// Hall of Fame entries, which go back to the stock ones.
        /// </summary>
        public void DeleteProfile(string name)
        {
            if (name.Length == 0 || !Directory.Exists(SaveDirectory))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(SaveDirectory))
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                bool own = stem.Equals(name, StringComparison.OrdinalIgnoreCase) || stem.StartsWith(name + "~", StringComparison.OrdinalIgnoreCase);
                if (own && !Path.GetFileName(file).Equals(HallOfFameFile, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                }
            }
            var hof = LoadHallOfFame();
            var stock = StockHallOfFame();
            bool changed = false;
            for (int i = 0; i < hof.Length; i++)
            {
                if (hof[i].LevelName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    (hof[i].LevelName, hof[i].LevelScore, changed) = (stock[i].LevelName, stock[i].LevelScore, true);
                }
                if (hof[i].CumulativeName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    (hof[i].CumulativeName, hof[i].CumulativeScore, changed) = (stock[i].CumulativeName, stock[i].CumulativeScore, true);
                }
            }
            if (changed)
            {
                SaveHallOfFame(hof);
            }
        }

        public IEnumerable<string> KnownPlayers() =>
            Directory.Exists(SaveDirectory)
                ? Directory.GetFiles(SaveDirectory, "*.thg").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)
                : Enumerable.Empty<string>();

        private static string CString(ReadOnlySpan<byte> span)
        {
            int end = span.IndexOf((byte)0);
            return Encoding.Latin1.GetString(end < 0 ? span : span[..end]);
        }

        private static void WriteCString(Span<byte> span, string text)
        {
            span.Clear();
            byte[] bytes = Encoding.Latin1.GetBytes(text);
            bytes.AsSpan(0, Math.Min(bytes.Length, span.Length - 1)).CopyTo(span);
        }

        private static byte[]? ReadFile(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Could not read {Path}", path);
                return null;
            }
        }

        private static void WriteFile(string path, byte[] data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not write {Path}", path);
            }
        }
    }
}
