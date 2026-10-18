using Thexder.Core.Game;

namespace Thexder.Feature.Procedural
{
    /// <summary>Fills a finished level with the world's enemies and pickups.</summary>
    internal sealed class Populator
    {
        private const double ClassicArea = 320 * 68;

        private const int EnergyCell = 0x12;

        private readonly GenRequest req;
        private readonly WorldStyle style;
        private readonly ClassicLibrary lib;
        private readonly Blueprint bp;
        private readonly Walker walker;
        private readonly ProcRandom rng;
        private readonly LevelData data;
        private readonly bool[] taken;

        public Populator(GenRequest req, WorldStyle style, ClassicLibrary lib, Blueprint bp, Walker walker, ProcRandom rng, LevelData data)
        {
            this.req = req;
            this.style = style;
            this.lib = lib;
            this.bp = bp;
            this.walker = walker;
            this.rng = rng;
            this.data = data;
            taken = new bool[bp.W * bp.H];
            Take(bp.StartX - 2, bp.StartY - 2, 8, 10);
            Take(bp.ExitX - 1, bp.ExitY - 1, 8, 8);
        }

        private double Area => bp.W * bp.H / ClassicArea;

        public void Apply()
        {
            if (req.Boss)
            {
                Bosses();
            }
            EnergyCellAtExit();
            Pickups();
            Enemies();
        }

        private bool Solid(int x, int y)
        {
            if (!bp.In(x, y))
            {
                return true;
            }
            int i = (y * bp.W + x) * 4;
            return data.Cells[i] != 0 && (data.Cells[i + 3] & 0xc0) == 0;
        }

        private bool Open(int x, int y, int w, int h)
        {
            for (int cy = y; cy < y + h; cy++)
            {
                for (int cx = x; cx < x + w; cx++)
                {
                    if (!bp.In(cx, cy) || Solid(cx, cy) || taken[cy * bp.W + cx])
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private void Take(int x, int y, int w, int h)
        {
            for (int cy = Math.Max(0, y); cy < Math.Min(bp.H, y + h); cy++)
            {
                for (int cx = Math.Max(0, x); cx < Math.Min(bp.W, x + w); cx++)
                {
                    taken[cy * bp.W + cx] = true;
                }
            }
        }

        private void Pickups()
        {
            var stands = walker.Stands;
            if (stands.Count == 0)
            {
                return;
            }
            var wanted = new List<(int Type, int Amount)>();
            // The same world's same level at the same difficulty: its pickups, as many again per classic map area
            var source = lib.ItemsAt(req.World, req.Level, req.Difficulty).Where(i => i.Type != EnergyCell).ToList();
            double scale = Area * (req.Difficulty <= 1 ? 1.1 : 1.0);
            foreach (var item in source)
            {
                int n = (int)(scale) + (rng.NextDouble() < scale - (int)scale ? 1 : 0);
                for (int k = 0; k < n; k++)
                {
                    wanted.Add((item.Type, item.Amount));
                }
            }
            // A minimum of energy and laser ammunition (both are on every classic level)
            int area = Math.Max(1, (int)Math.Round(Area));
            while (wanted.Count(p => p.Type == 0x15) < 2 * area)
            {
                wanted.Add((0x15, 25));
            }
            while (wanted.Count(p => p.Type == 1) < 2 * area)
            {
                wanted.Add((1, 25));
            }
            rng.Shuffle(wanted);
            foreach (var (type, amount) in wanted)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    var (sx, sy) = stands[rng.Next(stands.Count)];
                    if (Math.Abs(sx - bp.StartX) < 10 && Math.Abs(sy - bp.StartY) < 8)
                    {
                        continue;
                    }
                    if (TryPickup(sx + 1, sy + 4, type, amount))
                    {
                        break;
                    }
                }
            }
            foreach (var room in bp.Rooms)
            {
                if (room.Vault)
                {
                    StoreRoom(room);
                }
            }
        }

        /// <summary>A 2 x 2 pickup with its top-left cell at (x, y), lying on the ground.</summary>
        private bool TryPickup(int x, int y, int type, int amount)
        {
            if (!Open(x - 1, y - 1, 4, 3) || !(Solid(x, y + 2) || Solid(x + 1, y + 2)))
            {
                return false;
            }
            int i = (y * bp.W + x) * 4;
            data.Cells[i + 3] = (byte)((data.Cells[i + 3] & 0xc0) | (type & 0x3f));
            data.Cells[i + 1] = (byte)((data.Cells[i + 1] & 0x80) | (Math.Min(amount, 127) & 0x7f));
            Take(x - 2, y - 2, 6, 5);
            return true;
        }

        /// <summary>Extra cells of a level (World 7 level 4 hides a row of them) go to its store rooms.</summary>
        private void EnergyCellAtExit()
        {
            int cells = lib.ItemsAt(req.World, req.Level, req.Difficulty).Count(i => i.Type == EnergyCell);
            extraCells = Math.Max(0, cells - 1);
            if (cells == 0)
            {
                return;
            }
            int ex = bp.ExitX, ey = bp.ExitY;
            foreach (var (sx, sy) in walker.Stands.OrderBy(s => Math.Abs(s.X - ex) + Math.Abs(s.Y - ey)).Take(400))
            {
                if (TryPickupNearExit(sx + 1, sy + 4))
                {
                    return;
                }
            }
        }

        private bool TryPickupNearExit(int x, int y)
        {
            // Next to the exit itself is fine (the exit's own cells are kept free for the robot)
            bool wasTaken = false;
            if (bp.In(x, y) && taken[y * bp.W + x])
            {
                wasTaken = true;
            }
            if (wasTaken || !Open(x, y, 2, 2) || !(Solid(x, y + 2) || Solid(x + 1, y + 2)))
            {
                return false;
            }
            int i = (y * bp.W + x) * 4;
            data.Cells[i + 3] = (byte)((data.Cells[i + 3] & 0xc0) | EnergyCell);
            data.Cells[i + 1] = (byte)((data.Cells[i + 1] & 0x80) | 1);
            Take(x - 2, y - 2, 6, 5);
            return true;
        }

        private int extraCells;

        private void StoreRoom(Room room)
        {
            // The level's own pickups at this difficulty only, not ammunition for the laser
            var source = lib.ItemsAt(req.World, req.Level, req.Difficulty).Where(i => i.Type != EnergyCell && i.Type != 1).ToList();
            if (extraCells > 0)
            {
                source.Add(new ItemInstance(EnergyCell, 1, 0));
            }
            if (source.Count == 0)
            {
                return;
            }
            int n = rng.Range(2, 4);
            for (int k = 0; k < n; k++)
            {
                var item = source[rng.Next(source.Count)];
                int type = item.Type;
                int amount = item.Amount;
                if (type == EnergyCell)
                {
                    if (extraCells == 0)
                    {
                        continue;
                    }
                    extraCells--;
                }
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    int x = rng.Range(room.X + 3, room.Right - 5);
                    if (TryPickup(x, room.FloorTop - 2, type, amount))
                    {
                        break;
                    }
                }
            }
        }

        private void Enemies()
        {
            // The same world's same level at the same difficulty (its masks decide which enemies a difficulty has), as
            // many again per classic map area; each one a copy of one of them, standing where it stood
            var source = lib.EnemiesAt(req.World, req.Level, req.Difficulty).ToList();
            if (source.Count == 0)
            {
                return;
            }
            double count = source.Count * Area * (1 + 0.06 * Math.Max(0, req.Stage - 10));
            int n = Math.Min((int)Math.Round(count), 2000);
            int safe = Math.Min(40, bp.W / 6);
            for (int k = 0; k < n; k++)
            {
                var (template, perch) = source[k < source.Count * (int)Math.Max(1, Math.Floor(Area)) ? k % source.Count : rng.Next(source.Count)];
                int type = template.Type;
                var kind = EnemyTypeTable.Build(type, 8);
                if (kind.IsPlaceholder || kind.W <= 0 || kind.H <= 0)
                {
                    continue;
                }
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    if (FindSpot(perch, kind.W, kind.H, out int x, out int y) && Math.Abs(x - bp.StartX) > safe
                        && (template.Mode != 7 || PatrolFloorUnbroken(x, y, kind.W, kind.H)))
                    {
                        Add(template, x, y, kind.W, kind.H);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// A horizontal patroller (mode 7, like the Driller tank) ignores gravity: it drives left and right until
        /// something stops it, hovering over any gap.
        /// </summary>
        private bool PatrolFloorUnbroken(int x, int y, int w, int h)
        {
            bool onFloor = false;
            for (int c = 0; c < w; c++)
            {
                onFloor |= Solid(x + c, y + h);
            }
            if (!onFloor)
            {
                return true;
            }
            bool BoxClear(int bx)
            {
                for (int r = 0; r < h; r++)
                {
                    if (Solid(bx, y + r) || Solid(bx + w - 1, y + r))
                    {
                        return false;
                    }
                }
                return true;
            }
            int left = x, right = x;
            while (left > 1 && BoxClear(left - 1) && x - left < 400)
            {
                left--;
            }
            while (right + w < bp.W - 1 && BoxClear(right + 1) && right - x < 400)
            {
                right++;
            }
            for (int c = left; c < right + w; c++)
            {
                if (!Solid(c, y + h))
                {
                    return false;
                }
            }
            return true;
        }

        private void Add(EnemyRecord template, int x, int y, int w, int h)
        {
            var t = new EnemyRecord();
            t.CopyFrom(template);
            t.State = EnemyRecord.Alive;
            t.X = x;
            t.Y = y;
            t.W = w;
            t.H = h;
            t.DiffMask = 0;
            if (t.Mode == 7)
            {
                t.Dir = rng.Chance(0.5) ? 1 : 2;
            }
            data.Enemies.Add(t);
            Take(x - 1, y - 1, w + 2, h + 2);
        }

        private bool FindSpot(Perch perch, int w, int h, out int x, out int y)
        {
            x = rng.Range(3, bp.W - w - 3);
            y = rng.Range(3, bp.H - h - 4);
            switch (perch)
            {
                case Perch.Floor:
                {
                    // Drop from a random point to the floor below it
                    while (y + h < bp.H && !Solid(x, y + h) && !Solid(x + w - 1, y + h))
                    {
                        y++;
                    }
                    int under = 0;
                    for (int c = 0; c < w; c++)
                    {
                        under += Solid(x + c, y + h) ? 1 : 0;
                    }
                    return under * 2 >= w && Open(x, y, w, h);
                }
                case Perch.Ceiling:
                {
                    while (y > 1 && !Solid(x, y - 1) && !Solid(x + w - 1, y - 1))
                    {
                        y--;
                    }
                    int over = 0;
                    for (int c = 0; c < w; c++)
                    {
                        over += Solid(x + c, y - 1) ? 1 : 0;
                    }
                    return over * 2 >= w && Open(x, y, w, h) && Open(x, y + h, w, 4);
                }
                case Perch.Wall:
                {
                    bool left = true, right = true;
                    for (int r = 0; r < h; r++)
                    {
                        left &= Solid(x - 1, y + r);
                        right &= Solid(x + w, y + r);
                    }
                    if (!left && !right)
                    {
                        // Slide sideways to the nearest wall
                        int dx = rng.Chance(0.5) ? -1 : 1;
                        for (int s = 0; s < 20 && Open(x + dx, y, w, h); s++)
                        {
                            x += dx;
                        }
                        left = right = true;
                        for (int r = 0; r < h; r++)
                        {
                            left &= Solid(x - 1, y + r);
                            right &= Solid(x + w, y + r);
                        }
                    }
                    return (left || right) && Open(x, y, w, h);
                }
                default:
                    return Open(x - 1, y - 1, w + 2, h + 2) && Open(x, y + h, w, 3);
            }
        }

        // ---- The boss ------------------------------------------------------------------------------------

        private void Bosses()
        {
            var arena = bp.ArenaRoom;
            if (arena == null || style.Bosses.Count == 0)
            {
                return;
            }
            int floorX = arena.X + 6;
            foreach (var boss in style.Bosses)
            {
                var kind = EnemyTypeTable.Build(boss.Type, 8);
                int w = Math.Max(1, kind.W), h = Math.Max(1, kind.H);
                bool flyer = kind.Mode == 0 && boss.Type != 0x26 && boss.Type != 0x1b; // Pulsar Czar floats; the dragon and Bug Breath stand
                int x, y;
                if (flyer)
                {
                    x = arena.X + (arena.W - w) / 2;
                    y = arena.Top + (arena.FloorTop - arena.Top - h) / 3;
                }
                else
                {
                    x = Math.Min(floorX, arena.Right - w - 12);
                    y = arena.FloorTop - h;
                    floorX += w + 3;
                }
                x = Math.Clamp(x, arena.X, Math.Max(arena.X, arena.Right - w));
                y = Math.Clamp(y, arena.Top, arena.FloorTop - h);
                bp.Clear(x, y, w, h);
                for (int cy = y; cy < y + h; cy++)
                {
                    for (int cx = x; cx < x + w; cx++)
                    {
                        int i = (cy * bp.W + cx) * 4;
                        if ((data.Cells[i + 3] & 0x80) == 0)
                        {
                            data.Cells[i] = 0; // Nothing solid inside the boss
                        }
                    }
                }
                var t = new EnemyRecord();
                t.CopyFrom(boss);
                t.X = x;
                t.Y = y;
                t.DiffMask = 0;
                data.Enemies.Add(t);
                Take(x - 2, y - 2, w + 4, h + 4);
            }
        }
    }
}
