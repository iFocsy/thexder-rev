using Thexder.Core.Assets;
using Thexder.Core.Game;

namespace Thexder.Feature.Procedural
{
    /// <summary>
    /// A tile choice learned from the classic levels: tile numbers with how often the designers used them.
    /// </summary>
    internal sealed class TileOdds
    {
        private readonly Dictionary<int, int> counts = new();

        public int Total { get; private set; }

        public void Add(int tile, int n = 1)
        {
            counts[tile] = counts.GetValueOrDefault(tile) + n;
            Total += n;
        }

        public bool IsEmpty => Total == 0;

        public int Count(int tile) => counts.GetValueOrDefault(tile);

        /// <summary>The most used tile (or the fallback).</summary>
        public int Top(int fallback) => counts.Count == 0 ? fallback : counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;

        /// <summary>A tile in proportion to its use, ignoring the rarely used ones (under 3 %).</summary>
        public int Pick(ProcRandom rng, int fallback)
        {
            if (counts.Count == 0)
            {
                return fallback;
            }
            var common = counts.Where(kv => kv.Value * 33 >= Total).OrderBy(kv => kv.Key).ToList();
            return common[rng.Weighted(common.Select(kv => (double)kv.Value).ToList())].Key;
        }

        public void Merge(TileOdds other)
        {
            foreach (var kv in other.counts)
            {
                Add(kv.Key, kv.Value);
            }
        }
    }

    /// <summary>
    /// A pickup of an classic level: type, amount, and the difficulties that remove it (bit per difficulty).
    /// </summary>
    internal sealed record ItemInstance(int Type, int Amount, int DiffMask)
    {
        public bool At(int difficulty) => (DiffMask & (1 << difficulty)) == 0;
    }

    /// <summary>Where an enemy type is found in the classic levels.</summary>
    internal enum Perch
    {
        Floor,
        Ceiling,
        Air,
        Wall,
    }

    internal sealed class WorldStyle
    {
        public int World { get; init; }

        public int TileSheet { get; set; }

        public int[] Backgrounds { get; } = new int[5];

        /// <summary>The player record of the world's first level (a new game's form and facing).</summary>
        public byte[] PlayerRecord { get; set; } = new byte[PlayerState.RecordSize];

        /// <summary>
        /// Solid structure tiles by which of the four neighbours (N, E, S, W = bits 8, 4, 2, 1) are solid.
        /// </summary>
        public TileOdds[] Shape { get; } = Enumerable.Range(0, 16).Select(_ => new TileOdds()).ToArray();

        /// <summary>The tiles found under a beam's top tile (pairs of a 2-row beam).</summary>
        public Dictionary<int, TileOdds> Below { get; } = new();

        /// <summary>A run tile's caps: the tile at its left end and at its right end.</summary>
        public Dictionary<int, TileOdds> LeftCap { get; } = new();

        public Dictionary<int, TileOdds> RightCap { get; } = new();

        /// <summary>Background (decoration) tiles and how often they are used.</summary>
        public TileOdds BackgroundTiles { get; } = new();

        /// <summary>Solid tiles and how often they are used (to spot hazards, conveyors, rock).</summary>
        public TileOdds SolidTiles { get; } = new();

        /// <summary>Destructible (shoot-away) tiles.</summary>
        public TileOdds ShootTiles { get; } = new();

        /// <summary>Enemy types and how many of each the five levels hold (bosses excluded).</summary>
        public Dictionary<int, int> Roster { get; } = new();

        /// <summary>The boss group of the world's fifth level, positions relative to the first boss.</summary>
        public List<EnemyRecord> Bosses { get; } = new();

        /// <summary>Pickup types: how many per level and their usual amount.</summary>
        public Dictionary<int, (double PerLevel, int Amount)> Items { get; } = new();

        public double EnemiesPerLevel { get; set; }

        public double PlatformsPerLevel { get; set; }

        public bool Uses(int tile, int atLeast = 20) => BackgroundTiles.Count(tile) + SolidTiles.Count(tile) >= atLeast;

        public bool UsesBackground(int tile, int atLeast = 20) => BackgroundTiles.Count(tile) >= atLeast;
    }

    /// <summary>Built once per data folder; the levels are read from the player's own game data.</summary>
    internal sealed class ClassicLibrary
    {
        private static readonly object Gate = new();
        private static ClassicLibrary? cached;
        private static string cachedDir = "";

        private static readonly HashSet<int> StructureTiles = new(new[] { 1, 2, 3, 4, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 234, 240, 246 });

        private ClassicLibrary()
        {
        }

        public WorldStyle[] Worlds { get; } = new WorldStyle[11];

        public Dictionary<(int World, int Type), EnemyRecord> Templates { get; } = new();

        public Dictionary<int, EnemyRecord> AnyTemplate { get; } = new();

        public Dictionary<int, Perch> Perches { get; } = new();

        /// <summary>Every enemy of the classic levels with where it stands, by world and type.</summary>
        public Dictionary<(int World, int Type), List<(EnemyRecord Record, Perch Perch)>> Instances { get; } = new();

        public Dictionary<int, List<(EnemyRecord Record, Perch Perch)>> AnyInstances { get; } = new();

        public List<(EnemyRecord Record, Perch Perch)>? InstancesOf(int world, int type) =>
            Instances.TryGetValue((world, type), out var list) ? list : AnyInstances.GetValueOrDefault(type);

        public Dictionary<(int World, int Level), List<(EnemyRecord Record, Perch Perch)>> LevelEnemies { get; } = new();

        public Dictionary<(int World, int Level), List<ItemInstance>> LevelItems { get; } = new();

        public IEnumerable<(EnemyRecord Record, Perch Perch)> EnemiesAt(int world, int level, int difficulty) =>
            LevelEnemies.GetValueOrDefault((world, level), new()).Where(e => (e.Record.DiffMask & (1 << difficulty)) == 0);

        public IEnumerable<ItemInstance> ItemsAt(int world, int level, int difficulty) =>
            LevelItems.GetValueOrDefault((world, level), new()).Where(i => i.At(difficulty));

        /// <summary>A moving platform record to copy.</summary>
        public EnemyRecord? PlatformTemplate { get; private set; }

        /// <summary>Structure tiles over all worlds, for shapes a world never used.</summary>
        public TileOdds[] GlobalShape { get; } = Enumerable.Range(0, 16).Select(_ => new TileOdds()).ToArray();

        public static ClassicLibrary For(string dataDirectory)
        {
            lock (Gate)
            {
                if (cached == null || !string.Equals(cachedDir, dataDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    cached = Build(dataDirectory);
                    cachedDir = dataDirectory;
                }
                return cached;
            }
        }

        public EnemyRecord? Template(int world, int type) =>
            Templates.TryGetValue((world, type), out var t) ? t : AnyTemplate.GetValueOrDefault(type);

        private static ClassicLibrary Build(string dataDirectory)
        {
            var lib = new ClassicLibrary();
            var perchCounts = new Dictionary<int, int[]>();
            for (int w = 1; w <= 10; w++)
            {
                var style = new WorldStyle { World = w };
                lib.Worlds[w] = style;
                int enemyCount = 0, platforms = 0;
                var itemCounts = new Dictionary<int, int>();
                var itemAmounts = new Dictionary<int, Dictionary<int, int>>();
                for (int l = 1; l <= 5; l++)
                {
                    string path = Path.Combine(dataDirectory, LevelFile.FileName(w, l));
                    if (!File.Exists(path))
                    {
                        continue;
                    }
                    var file = LevelFile.Load(path);
                    var header = new LevelHeader();
                    header.Load(file.Header);
                    style.TileSheet = header.TileSheet;
                    style.Backgrounds[l - 1] = header.Background;
                    if (l == 1)
                    {
                        style.PlayerRecord = file.Thexder.ToArray();
                    }
                    var cells = file.Cells;
                    LearnTiles(style, lib.GlobalShape, cells);

                    var records = EnemyRecord.ReadArray(file.Enemies, LevelFile.EnemyCount);
                    var levelEnemies = lib.LevelEnemies[(w, l)] = new();
                    var levelItems = lib.LevelItems[(w, l)] = new();
                    EnemyRecord? firstBoss = null;
                    for (int k = 1; k < records.Length; k++)
                    {
                        var t = records[k];
                        if (t.State != EnemyRecord.Alive || t.Type >= EnemyTypeTable.Count)
                        {
                            continue;
                        }
                        if (EnemyTypeTable.IsBossType(t.Type))
                        {
                            if (l == 5)
                            {
                                var b = new EnemyRecord();
                                b.CopyFrom(t);
                                firstBoss ??= b;
                                style.Bosses.Add(b);
                            }
                            continue;
                        }
                        enemyCount++;
                        style.Roster[t.Type] = style.Roster.GetValueOrDefault(t.Type) + 1;
                        if (!lib.Templates.ContainsKey((w, t.Type)))
                        {
                            var copy = new EnemyRecord();
                            copy.CopyFrom(t);
                            lib.Templates[(w, t.Type)] = copy;
                            lib.AnyTemplate.TryAdd(t.Type, copy);
                        }
                        var perch = PerchOf(cells, t);
                        var counts = perchCounts.TryGetValue(t.Type, out var c) ? c : perchCounts[t.Type] = new int[4];
                        counts[(int)perch]++;
                        var instance = new EnemyRecord();
                        instance.CopyFrom(t);
                        (lib.Instances.TryGetValue((w, t.Type), out var list) ? list : lib.Instances[(w, t.Type)] = new()).Add((instance, perch));
                        (lib.AnyInstances.TryGetValue(t.Type, out var any) ? any : lib.AnyInstances[t.Type] = new()).Add((instance, perch));
                        levelEnemies.Add((instance, perch));
                    }
                    // The boss group's positions become relative to the first boss.
                    if (firstBoss != null)
                    {
                        int bx = firstBoss.X, by = firstBoss.Y;
                        foreach (var b in style.Bosses)
                        {
                            b.X -= bx;
                            b.Y -= by;
                        }
                    }

                    foreach (var p in EnemyRecord.ReadArray(file.Platforms, LevelFile.PlatformCount))
                    {
                        if (p.State != 0)
                        {
                            platforms++;
                            if (lib.PlatformTemplate == null)
                            {
                                lib.PlatformTemplate = new EnemyRecord();
                                lib.PlatformTemplate.CopyFrom(p);
                            }
                        }
                    }
                    for (int k = 0; k < LevelFile.ObjectCount; k++)
                    {
                        var o = new LevelObject();
                        o.Read(file.Objects.AsSpan(k * LevelObject.Size, LevelObject.Size));
                        if (o.Kind != LevelObject.KindPickup || o.Type == 0x13 || o.Type == 0x1a)
                        {
                            continue; // Teleporters and switches belong to the level's design
                        }
                        itemCounts[o.Type] = itemCounts.GetValueOrDefault(o.Type) + 1;
                        levelItems.Add(new ItemInstance(o.Type, Math.Max(1, o.Amount), o.DiffMask));
                        var amounts = itemAmounts.TryGetValue(o.Type, out var a) ? a : itemAmounts[o.Type] = new Dictionary<int, int>();
                        amounts[o.Amount] = amounts.GetValueOrDefault(o.Amount) + 1;
                    }
                }
                style.EnemiesPerLevel = enemyCount / 5.0;
                style.PlatformsPerLevel = platforms / 5.0;
                foreach (var (type, n) in itemCounts)
                {
                    int amount = itemAmounts[type].OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                    style.Items[type] = (n / 5.0, Math.Max(1, amount));
                }
            }
            foreach (var (type, counts) in perchCounts)
            {
                int best = 0;
                for (int i = 1; i < 4; i++)
                {
                    if (counts[i] > counts[best])
                    {
                        best = i;
                    }
                }
                lib.Perches[type] = (Perch)best;
            }
            return lib;
        }

        private static bool Visible(byte[] cells, int x, int y)
        {
            if (x < 0 || y < 0 || x >= LevelFile.MapWidth || y >= LevelFile.MapHeight)
            {
                return true;
            }
            int i = (y * LevelFile.MapWidth + x) * 4;
            return cells[i] != 0 && (cells[i + 3] & 0x80) == 0;
        }

        private static bool Solid(byte[] cells, int x, int y)
        {
            if (x < 0 || y < 0 || x >= LevelFile.MapWidth || y >= LevelFile.MapHeight)
            {
                return true;
            }
            int i = (y * LevelFile.MapWidth + x) * 4;
            return cells[i] != 0 && (cells[i + 3] & 0xc0) == 0;
        }

        public static int ShapeOf(Func<int, int, bool> solid, int x, int y) =>
            (solid(x, y - 1) ? 8 : 0) | (solid(x + 1, y) ? 4 : 0) | (solid(x, y + 1) ? 2 : 0) | (solid(x - 1, y) ? 1 : 0);

        private static void LearnTiles(WorldStyle style, TileOdds[] global, byte[] cells)
        {
            int w = LevelFile.MapWidth, h = LevelFile.MapHeight;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    int tile = cells[i];
                    if (tile == 0)
                    {
                        continue;
                    }
                    if ((cells[i + 3] & 0x80) != 0)
                    {
                        style.BackgroundTiles.Add(tile);
                        continue;
                    }
                    if ((cells[i + 3] & 0x40) == 0)
                    {
                        style.SolidTiles.Add(tile);
                    }
                    if ((cells[i + 2] & 0x80) != 0)
                    {
                        style.ShootTiles.Add(tile);
                    }
                    if (!StructureTiles.Contains(tile) || (cells[i + 2] & 0x80) != 0)
                    {
                        continue;
                    }
                    int shape = ShapeOf((cx, cy) => Visible(cells, cx, cy), x, y);
                    style.Shape[shape].Add(tile);
                    global[shape].Add(tile);
                    // A 2-row beam: top tile over a bottom tile
                    if (shape == 0b0111 && y + 1 < h && ShapeOf((cx, cy) => Visible(cells, cx, cy), x, y + 1) == 0b1101)
                    {
                        Odds(style.Below, tile).Add(cells[i + w * 4]);
                    }
                    // Caps: the tile at the end of a run, keyed by the run tile next to it
                    if ((shape & 0b0101) == 0b0100 && x + 1 < w)
                    {
                        Odds(style.LeftCap, cells[i + 4]).Add(tile);
                    }
                    if ((shape & 0b0101) == 0b0001 && x > 0)
                    {
                        Odds(style.RightCap, cells[i - 4]).Add(tile);
                    }
                }
            }
        }

        private static TileOdds Odds(Dictionary<int, TileOdds> map, int key) =>
            map.TryGetValue(key, out var o) ? o : map[key] = new TileOdds();

        private static Perch PerchOf(byte[] cells, EnemyRecord t)
        {
            bool below = false, above = false, side = false;
            for (int c = 0; c < t.W; c++)
            {
                below |= Solid(cells, t.X + c, t.Y + t.H);
                above |= Solid(cells, t.X + c, t.Y - 1);
            }
            for (int r = 0; r < t.H; r++)
            {
                side |= Solid(cells, t.X - 1, t.Y + r) || Solid(cells, t.X + t.W, t.Y + r);
            }
            return below ? Perch.Floor : above ? Perch.Ceiling : side ? Perch.Wall : Perch.Air;
        }
    }
}
