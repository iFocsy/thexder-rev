using Serilog;
using Thexder.Core.Game;

namespace Thexder.Feature.Procedural
{
    /// <summary>
    /// Builds a level from a <see cref="GenRequest"/>: layout (Architect), tiles (Skinner), decoration (Decorator), a
    /// reachability check (Walker) and enemies and pickups (Populator).
    /// </summary>
    internal static class LevelGenerator
    {
        public static LevelData Generate(GenRequest req, ClassicLibrary lib, int maxAttempts = 8)
        {
            int world = Math.Clamp(req.World, 1, 10);
            req = req with { World = world, Level = Math.Clamp(req.Level, 1, 5) };
            var style = lib.Worlds[world];
            LevelData? last = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                ulong seed = req.Seed + (ulong)attempt * 0x9E3779B97F4A7C15UL;
                var architect = new Architect(req, style, ProcRandom.For(seed, "layout"));
                var bp = architect.Build();
                LevelData data;
                Walker walker;
                bool finishable;
                int repairs = 0;
                while (true)
                {
                    data = new LevelData(bp.W, bp.H)
                    {
                        TileSheet = style.TileSheet,
                        Background = style.Backgrounds[(req.Level - 1) % 5],
                        StartX = bp.StartX,
                        StartY = bp.StartY,
                        ExitX = bp.ExitX,
                        ExitY = bp.ExitY,
                        PlayerRecord = style.PlayerRecord.ToArray(),
                        Title = req.Title,
                    };
                    new Skinner(bp, style, lib, ProcRandom.For(seed, "skin")).Apply(data.Cells);
                    new Decorator(bp, style, ProcRandom.For(seed, "decor"), data.Cells).Apply();
                    walker = new Walker(bp.W, bp.H, data.Cells) { Plane = req.Stage >= 2, Tank = req.Stage >= 3 };
                    finishable = walker.Explore(bp.StartX, bp.StartY, bp.ExitX, bp.ExitY);
                    if (finishable || repairs >= 12)
                    {
                        break;
                    }
                    var reached = RoomsReached(bp, walker);
                    if (!architect.Repair(room => reached.Contains(room)))
                    {
                        break;
                    }
                    repairs++;
                }
                LastRepairs = repairs;
                new Populator(req, style, lib, bp, walker, ProcRandom.For(seed, "populate"), data).Apply();
                last = data;
                if (finishable)
                {
                    LastAttempts = attempt + 1;
                    return data;
                }
                Log.Warning("Generated level {World}-{Level} seed {Seed} attempt {Attempt}: exit not reachable on foot, rebuilding", world, req.Level, req.Seed, attempt);
            }
            LastAttempts = 9;
            return last!;
        }

        /// <summary>The rooms where the robot can stand somewhere.</summary>
        private static HashSet<Room> RoomsReached(Blueprint bp, Walker walker)
        {
            var reached = new HashSet<Room>();
            foreach (var room in bp.Rooms)
            {
                foreach (var (x, y) in walker.Stands)
                {
                    if (x + 1 >= room.X && x + 1 < room.Right && y + 5 >= room.Top && y + 5 < room.FloorTop)
                    {
                        reached.Add(room);
                        break;
                    }
                }
            }
            return reached;
        }

        /// <summary>Diagnostics: repairs the last finished layout on this thread needed.</summary>
        [ThreadStatic]
        private static int lastRepairs;

        public static int LastRepairs { get => lastRepairs; private set => lastRepairs = value; }

        /// <summary>Diagnostics: how many layouts the last level on this thread needed (9 = none was finishable).</summary>
        [ThreadStatic]
        private static int lastAttempts;

        public static int LastAttempts { get => lastAttempts; private set => lastAttempts = value; }

        /// <summary>
        /// Whether the robot can get from the level's start to its exit with the forms of a campaign stage (stage 1:
        /// walking only; 2: the plane too; 3 on: the tank too).
        /// </summary>
        public static bool Finishable(LevelData level, int stage = 1) =>
            new Walker(level.Width, level.Height, level.Cells) { Plane = stage >= 2, Tank = stage >= 3 }
                .Explore(level.StartX, level.StartY, level.ExitX, level.ExitY);
    }
}
