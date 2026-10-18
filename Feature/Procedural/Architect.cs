namespace Thexder.Feature.Procedural
{
    /// <summary>
    /// Lays out a level: a grid of rooms (columns of 30-56 cells, rows about a floor apart, each room's floor a few cells
    /// higher or lower than its neighbours'), a winding main route from the start on the left to the exit on the right,
    /// extra links that make loops, store rooms behind destructible walls, and the structures that join the rooms - open
    /// floors with steps, walls, gates, stairs and ledges up through holes in the floors.
    /// </summary>
    internal sealed class Architect
    {
        private const int TopBorder = 2, BottomBorder = 3, SideBorder = 2;
        private const int LedgeRise = 7; // A ledge's height over the floor below it
        private const int TopJump = 9; // The highest jump onto a floor through a hole
        private const int MinClear = 11; // The lowest room (the robot is 6 cells tall)
        private const int TunnelHeight = 4; // The tank (3 tall) fits; the walker (6) and the plane (5) do not

        private enum Link
        {
            None,
            Open,
            Gate,
            /// <summary>A low tunnel through the wall: only the tank gets through.</summary>
            Tunnel,
        }

        private readonly GenRequest req;
        private readonly WorldStyle style;
        private readonly ProcRandom rng;
        private Blueprint bp = null!;
        private Room[,] rooms = null!;
        private int[,] thick = null!; // Floor thickness under each room
        private Link[,] east = null!; // Between (c, r) and (c + 1, r)
        private bool[,] south = null!; // Between (c, r) and (c, r + 1)
        private bool[,] shaft = null!; // A south link with nothing to climb: only the plane goes up
        private int cols, rows;
        private readonly List<(int C, int R)> route = new();
        private readonly Dictionary<Room, (int From, int To)> furnished = new();
        private readonly Dictionary<int, int> repairs = new();

        public Architect(GenRequest req, WorldStyle style, ProcRandom rng)
        {
            this.req = req;
            this.style = style;
            this.rng = rng;
        }

        private bool PlaneAvailable => req.Stage >= 2;

        private bool TankAvailable => req.Stage >= 3;

        public Blueprint Build()
        {
            var (w, h) = MapSizes.Of(req.Size);
            bp = new Blueprint(w, h);
            bp.Fill(0, 0, w, TopBorder, Mat.Mass);
            bp.Fill(0, h - BottomBorder, w, BottomBorder, Mat.Mass);
            bp.Fill(0, 0, SideBorder, h, Mat.Mass);
            bp.Fill(w - SideBorder, 0, SideBorder, h, Mat.Mass);
            MakeGrid();
            var (start, end) = MakeRoute();
            RequireForms();
            if (req.Boss && req.World == 10 && rows >= 3)
            {
                int c = end.C;
                for (int r = Math.Min(end.R, 2); r < Math.Max(end.R, 2); r++)
                {
                    south[c, r] = true;
                    rooms[c, r].OnPath = rooms[c, r + 1].OnPath = true;
                }
                end = (c, 2);
                route.Add(end);
            }
            ExtraLinks();
            ConnectStrays(start);
            if (req.Boss && rows > 1)
            {
                MakeArena(end);
            }
            MakeHalls(start, end);
            PlaceStartAndExit(start, end); // First, so stairs and the rest keep clear of them
            BuildFloors();
            BuildBoundaries();
            BuildAscents();
            bp.Clear(bp.ExitX, bp.ExitY, 6, 6);
            bp.Clear(bp.StartX, bp.StartY, 4, 6);
            foreach (var room in rooms)
            {
                Furnish(room);
            }
            bp.Rooms = rooms;
            return bp;
        }

        // ---- The grid ------------------------------------------------------------------------------------

        private void MakeGrid()
        {
            var widths = new List<int>();
            int x = SideBorder, right = bp.W - SideBorder;
            while (right - x > 0)
            {
                int cw = rng.Range(30, 56);
                if (req.Boss && right - x <= 112)
                {
                    cw = right - x <= 62 ? right - x : rng.Range(48, 56); // A wide last room for the arena
                }
                if (right - x - cw < 28)
                {
                    cw = right - x;
                }
                widths.Add(cw);
                x += cw;
            }
            cols = widths.Count;
            int inner = bp.H - TopBorder - BottomBorder;
            rows = Math.Max(1, (int)Math.Round(inner / 16.0));
            // Rows of different heights (12 to 20 cells of air), so the floors do not stack like shelves
            int total = inner - 2 * (rows - 1);
            var clears = new int[rows];
            for (int r = 0; r < rows; r++)
            {
                clears[r] = total / rows + (r == rows - 1 ? total % rows : 0);
            }
            for (int k = 0; k < rows * 3 && rows > 1; k++)
            {
                int i = rng.Next(rows), j = rng.Next(rows), d = rng.Range(1, 3);
                if (i != j && clears[i] - d >= 12 && clears[j] + d <= 20)
                {
                    clears[i] -= d;
                    clears[j] += d;
                }
            }
            var baseFloors = new int[rows];
            for (int r = 0, y = TopBorder; r < rows; r++)
            {
                baseFloors[r] = y + clears[r];
                y += clears[r] + 2;
            }
            rooms = new Room[cols, rows];
            thick = new int[cols, rows];
            var jitters = new int[cols, rows];
            x = SideBorder;
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    bool last = r == rows - 1;
                    // A floor often runs on at the same height into the next room: longer open stretches
                    jitters[c, r] = c > 0 && rng.Chance(0.55) ? jitters[c - 1, r] : last ? rng.Range(-3, 0) : rng.Range(-2, 2);
                    thick[c, r] = last ? 0 : rng.Chance(0.2) ? 3 : 2;
                    int top = r == 0 ? TopBorder : rooms[c, r - 1].FloorTop + thick[c, r - 1];
                    int floorTop = Math.Max(baseFloors[r] + jitters[c, r], top + MinClear);
                    if (last)
                    {
                        floorTop = Math.Min(floorTop, bp.H - BottomBorder);
                    }
                    rooms[c, r] = new Room { Col = c, Row = r, X = x, W = widths[c], Top = top, WallTop = top, FloorTop = floorTop };
                }
                x += widths[c];
            }
            east = new Link[cols, rows];
            south = new bool[cols, rows];
            shaft = new bool[cols, rows];
        }

        private ((int C, int R) Start, (int C, int R) End) MakeRoute()
        {
            int r = rng.Next(rows), c = 0;
            var start = (0, r);
            rooms[c, r].OnPath = true;
            route.Add((c, r));
            int verticalRun = 0;
            while (c < cols - 1)
            {
                bool canUp = r > 0 && !rooms[c, r - 1].OnPath;
                bool canDown = r < rows - 1 && !rooms[c, r + 1].OnPath;
                if (verticalRun < 2 && (canUp || canDown) && rng.Chance(rows > 2 ? 0.4 : 0.3))
                {
                    bool up = canUp && (!canDown || rng.Chance(0.5));
                    int nr = up ? r - 1 : r + 1;
                    south[c, Math.Min(r, nr)] = true;
                    shaft[c, Math.Min(r, nr)] = PlaneAvailable && rng.Chance(0.4);
                    r = nr;
                    verticalRun++;
                }
                else
                {
                    east[c, r] = TankAvailable && rng.Chance(0.2) ? Link.Tunnel : Link.Open;
                    c++;
                    verticalRun = 0;
                }
                rooms[c, r].OnPath = true;
                route.Add((c, r));
            }
            return (start, (c, r));
        }

        private void RequireForms()
        {
            if (!PlaneAvailable)
            {
                return;
            }
            var vertical = new List<(int C, int R)>();
            var horizontal = new List<(int C, int R)>();
            bool needed = false;
            for (int k = 1; k < route.Count; k++)
            {
                var (pc, pr) = route[k - 1];
                var (c, r) = route[k];
                if (pc == c && pr != r)
                {
                    needed |= shaft[c, Math.Min(pr, r)];
                    vertical.Add((c, Math.Min(pr, r)));
                }
                else if (pr == r && pc != c)
                {
                    needed |= east[Math.Min(pc, c), r] == Link.Tunnel;
                    horizontal.Add((Math.Min(pc, c), r));
                }
            }
            if (needed || !rng.Chance(0.8))
            {
                return;
            }
            bool tunnel = TankAvailable && horizontal.Count > 0 && (vertical.Count == 0 || rng.Chance(0.5));
            if (tunnel)
            {
                var (c, r) = rng.Pick(horizontal);
                east[c, r] = Link.Tunnel;
            }
            else if (vertical.Count > 0)
            {
                var (c, r) = rng.Pick(vertical);
                shaft[c, r] = true;
            }
        }

        private void ExtraLinks()
        {
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    if (c < cols - 1 && east[c, r] == Link.None)
                    {
                        double p = rng.NextDouble();
                        east[c, r] = p < 0.5 ? Link.Open : p < 0.58 ? Link.Gate : TankAvailable && p < 0.63 ? Link.Tunnel : Link.None;
                    }
                    if (r < rows - 1 && !south[c, r] && rng.Chance(0.22))
                    {
                        south[c, r] = true;
                        shaft[c, r] = PlaneAvailable && rng.Chance(0.3);
                    }
                }
            }
        }

        /// <summary>Every room joins the level; rooms joined only through gates become stores.</summary>
        private void ConnectStrays((int C, int R) start)
        {
            while (true)
            {
                var seen = Reach(start, gates: true);
                var strays = new List<(int C, int R)>();
                for (int c = 0; c < cols; c++)
                {
                    for (int r = 0; r < rows; r++)
                    {
                        if (!seen[c, r])
                        {
                            strays.Add((c, r));
                        }
                    }
                }
                if (strays.Count == 0)
                {
                    break;
                }
                rng.Shuffle(strays);
                bool linked = false;
                foreach (var (c, r) in strays)
                {
                    var options = new List<Action>();
                    if (c > 0 && seen[c - 1, r])
                    {
                        options.Add(() => east[c - 1, r] = rng.Chance(0.5) ? Link.Gate : Link.Open);
                    }
                    if (c < cols - 1 && seen[c + 1, r])
                    {
                        options.Add(() => east[c, r] = rng.Chance(0.5) ? Link.Gate : Link.Open);
                    }
                    if (r > 0 && seen[c, r - 1])
                    {
                        options.Add(() => south[c, r - 1] = true);
                    }
                    if (r < rows - 1 && seen[c, r + 1])
                    {
                        options.Add(() => south[c, r] = true);
                    }
                    if (options.Count > 0)
                    {
                        rng.Pick(options)();
                        linked = true;
                        break;
                    }
                }
                if (!linked)
                {
                    break;
                }
            }
            var open = Reach(start, gates: false);
            foreach (var room in rooms)
            {
                room.Vault = !open[room.Col, room.Row];
            }
        }

        private bool[,] Reach((int C, int R) start, bool gates)
        {
            var seen = new bool[cols, rows];
            var queue = new Queue<(int C, int R)>();
            queue.Enqueue(start);
            seen[start.C, start.R] = true;
            while (queue.Count > 0)
            {
                var (c, r) = queue.Dequeue();
                void Try(int nc, int nr, bool linked)
                {
                    if (linked && !seen[nc, nr])
                    {
                        seen[nc, nr] = true;
                        queue.Enqueue((nc, nr));
                    }
                }
                bool Passable(Link link) => link is Link.Open or Link.Tunnel || (gates && link == Link.Gate);
                if (c < cols - 1)
                {
                    Try(c + 1, r, Passable(east[c, r]));
                }
                if (c > 0)
                {
                    Try(c - 1, r, Passable(east[c - 1, r]));
                }
                if (r < rows - 1)
                {
                    Try(c, r + 1, south[c, r]);
                }
                if (r > 0)
                {
                    Try(c, r - 1, south[c, r - 1]);
                }
            }
            return seen;
        }

        // ---- Tall halls ----------------------------------------------------------------------------------

        private readonly Dictionary<(int C, int R), List<(int X, int W)>> balconies = new();

        private void MakeHalls((int C, int R) start, (int C, int R) end)
        {
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows - 1; r++)
                {
                    var upper = rooms[c, r];
                    var lower = rooms[c, r + 1];
                    bool eligible = south[c, r] && !shaft[c, r] && upper.W >= 34 && !upper.Hall && !lower.Hall && !upper.Arena && !lower.Arena
                        && (r == 0 || !south[c, r - 1]) && (c, r) != start && (c, r + 1) != start && (c, r) != end && (c, r + 1) != end;
                    if (!eligible || !rng.Chance(0.3))
                    {
                        continue;
                    }
                    upper.Hall = lower.Hall = true;
                    lower.Top = upper.Top;
                }
            }
        }

        /// <summary>The balconies of a hall's upper half: one at each side that has a door.</summary>
        private List<(int X, int W)> Balconies(int c, int r)
        {
            if (balconies.TryGetValue((c, r), out var list))
            {
                return list;
            }
            list = new List<(int X, int W)>();
            var room = rooms[c, r];
            int bw = Math.Min(14, room.W / 3);
            if (c > 0 && east[c - 1, r] != Link.None)
            {
                list.Add((room.X, bw));
            }
            if (c < cols - 1 && east[c, r] != Link.None)
            {
                list.Add((room.Right - bw, bw));
            }
            balconies[(c, r)] = list;
            return list;
        }

        /// <summary>The ways up to a hall's balconies: stairs where the hall is wide enough, else ledges.</summary>
        private void HallClimbs(int c, int r)
        {
            var upper = rooms[c, r];
            var lower = rooms[c, r + 1];
            int rise = lower.FloorTop - upper.FloorTop;
            bool stairsUsed = false;
            foreach (var (bx, bw) in Balconies(c, r))
            {
                bool left = bx == upper.X;
                int edge = left ? bx + bw : bx; // The balcony's inner end
                int span = 2 * rise;
                bool roomForStairs = left ? edge + span <= lower.Right - 6 : edge - span >= lower.X + 6;
                if (!stairsUsed && roomForStairs && rng.Chance(0.6))
                {
                    // Stairs rising towards the balcony, the top block level with it
                    for (int i = 0; i < rise; i++)
                    {
                        int k = rise - 1 - i;
                        int sx = left ? edge + 2 * k : edge - 2 - 2 * k;
                        bp.Fill(sx, lower.FloorTop - 1 - i, 2, 2, Mat.Step, overAir: true);
                    }
                    lower.Busy.Add(left ? (edge - 2, edge + span + 2) : (edge - span - 2, edge + 2));
                    stairsUsed = true;
                    continue;
                }
                // Ledges beside the balcony (not under it), the top one a 9-cell jump below it
                int count = Math.Max(1, (int)Math.Ceiling((rise - TopJump) / (double)LedgeRise));
                int topLedge = Math.Max(upper.FloorTop + TopJump, lower.FloorTop - LedgeRise * count);
                for (int i = 0; i < count; i++)
                {
                    int y = topLedge + i * LedgeRise;
                    if (y > lower.FloorTop - 3)
                    {
                        break;
                    }
                    int lx = left ? edge + 3 + 6 * i : edge - 11 - 6 * i;
                    lx = Math.Clamp(lx, lower.X + 2, lower.Right - 10);
                    bp.Fill(lx, y, 8, 1, Mat.Thin);
                    lower.Busy.Add((lx - 2, lx + 10));
                }
            }
        }

        // ---- The boss arena ------------------------------------------------------------------------------

        /// <summary>The exit room and the room above it (or below) become one tall hall.</summary>
        private void MakeArena((int C, int R) end)
        {
            var (c, r) = end;
            int upper = r > 0 ? r - 1 : r;
            var top = rooms[c, upper];
            var bottom = rooms[c, upper + 1];
            south[c, upper] = false; // No floor between them, so no ascent either
            top.Arena = bottom.Arena = true;
            bottom.Top = top.Top;
            bp.ArenaRoom = bottom;
        }

        private void BuildFloors()
        {
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    var room = rooms[c, r];
                    if (r == rows - 1)
                    {
                        if (room.FloorTop < bp.H - BottomBorder)
                        {
                            bp.Fill(room.X, room.FloorTop, room.W, bp.H - BottomBorder - room.FloorTop, Mat.Mass); // A raised ground
                        }
                        continue;
                    }
                    if (room.Arena && rooms[c, r + 1].Arena)
                    {
                        continue;
                    }
                    var mat = thick[c, r] == 2 ? Mat.Beam : Mat.Mass;
                    if (room.Hall && rooms[c, r + 1].Hall)
                    {
                        // A hall: only balconies at the doors of the upper half
                        foreach (var (bx, bw) in Balconies(c, r))
                        {
                            bp.Fill(bx, room.FloorTop, bw, thick[c, r], mat);
                        }
                        continue;
                    }
                    bp.Fill(room.X, room.FloorTop, room.W, thick[c, r], mat);
                }
            }
        }

        private void BuildBoundaries()
        {
            for (int c = 0; c < cols - 1; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    var a = rooms[c, r];
                    var b = rooms[c + 1, r];
                    if (b.Arena && r < rows - 1 && rooms[c + 1, r + 1].Arena)
                    {
                        // The arena's upper half: the wall reaches down to this row's floor level only
                    }
                    int hi = Math.Min(a.FloorTop, b.FloorTop), lo = Math.Max(a.FloorTop, b.FloorTop);
                    int top = Math.Min(a.WallTop, b.WallTop);
                    var link = east[c, r];
                    int wt = rng.Range(2, 4);
                    int wx = b.X - wt / 2;
                    if (link == Link.None)
                    {
                        bp.Fill(wx, top, wt, lo - top, Mat.Mass);
                        a.Busy.Add((wx - 4, b.X));
                        b.Busy.Add((b.X, wx + wt + 4));
                        continue;
                    }
                    if (link == Link.Gate)
                    {
                        wt = 3;
                        wx = b.X - 1;
                        int door = Math.Min(8, hi - top);
                        bp.Fill(wx, top, wt, hi - door - top, Mat.Mass);
                        bp.Fill(wx, hi - door, wt, door, Mat.Shoot);
                        if (lo > hi)
                        {
                            bp.Fill(wx, hi, wt, lo - hi, Mat.Mass);
                        }
                    }
                    else if (link == Link.Tunnel)
                    {
                        // A thick wall with a low tunnel at the higher floor's level, sometimes blocked by blocks the
                        // tank drills through
                        wt = rng.Range(4, 7);
                        wx = b.X - wt / 2;
                        bp.Fill(wx, top, wt, hi - TunnelHeight - top, Mat.Mass);
                        if (rng.Chance(0.4))
                        {
                            bp.Fill(wx, hi - TunnelHeight, wt, TunnelHeight, Mat.Shoot);
                        }
                        if (lo > hi)
                        {
                            bp.Fill(wx, hi, wt, lo - hi, Mat.Mass);
                        }
                    }
                    else if (hi - top >= 18 && rng.Chance(0.25))
                    {
                        // A partition hanging from the ceiling, with a passage under it
                        bp.Fill(wx, top, wt, hi - 9 - top, Mat.Mass);
                    }
                    // Steps up from the lower floor to the higher one, on the lower side
                    int d = lo - hi;
                    bool aLower = a.FloorTop > b.FloorTop;
                    int edge = link is Link.Gate or Link.Tunnel ? (aLower ? wx : wx + wt) : b.X;
                    for (int j = 0; j < d; j++)
                    {
                        int bx = aLower ? edge - 2 * j - 2 : edge + 2 * j;
                        bp.Fill(bx, hi + j, 2, 2, Mat.Step, overAir: true);
                    }
                    int reach = 2 * d + 4;
                    a.Busy.Add((Math.Min(a.Right - 4, aLower ? edge - reach : a.Right), a.Right));
                    b.Busy.Add((b.X, Math.Max(b.X + 4, aLower ? b.X : edge + reach)));
                }
            }
        }

        private void BuildAscents()
        {
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows - 1; r++)
                {
                    if (south[c, r] && rooms[c, r].Hall && rooms[c, r + 1].Hall)
                    {
                        HallClimbs(c, r);
                    }
                    else if (south[c, r] && shaft[c, r])
                    {
                        Shaft(rooms[c, r], rooms[c, r + 1], thick[c, r]);
                    }
                    else if (south[c, r])
                    {
                        // Which sides of the lower room are walls (the robot never needs to get past them)
                        bool wallLeft = c == 0 || east[c - 1, r + 1] == Link.None;
                        bool wallRight = c == cols - 1 || east[c, r + 1] == Link.None;
                        Ascent(rooms[c, r], rooms[c, r + 1], thick[c, r], wallLeft, wallRight);
                    }
                }
            }
        }

        /// <summary>
        /// A way up from the lower room to the upper one: stairs through a wide hole, or ledges under a hole.
        /// </summary>
        private void Ascent(Room upper, Room lower, int floor, bool wallLeft, bool wallRight)
        {
            int rise = lower.FloorTop - upper.FloorTop;
            int lo = lower.X + 3, hi = lower.Right - 3;
            if ((wallLeft || wallRight) && hi - lo >= 2 * rise + 2 && rng.Chance(0.6)
                && TryStairs(upper, lower, rise, floor, lo, hi, right: wallRight && (!wallLeft || rng.Chance(0.5))))
            {
                return;
            }
            Ledges(upper, lower, rise, floor, lo, hi);
        }

        private bool TryStairs(Room upper, Room lower, int rise, int floor, int lo, int hi, bool right)
        {
            int span = 2 * rise;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                // Against the wall the stairs rise towards
                int sx = right ? hi - span - attempt : lo + attempt;
                if (!lower.IsFree(sx - 2, sx + span + 2) || !upper.IsFree(sx, sx + span))
                {
                    continue;
                }
                // Blocks rise one cell per two columns; the floor above opens over the last ones
                for (int i = 0; i < rise; i++)
                {
                    int bx = right ? sx + 2 * i : sx + span - 2 - 2 * i;
                    bp.Fill(bx, lower.FloorTop - 1 - i, 2, 2, Mat.Step, overAir: true);
                }
                int open = Math.Min(rise, 6 + floor + 1) * 2 + 1;
                int hx = right ? sx + span - open : sx;
                bp.Clear(hx, upper.FloorTop, open, floor);
                lower.Busy.Add((sx - 2, sx + span + 2));
                upper.Busy.Add((hx - 1, hx + open + 1));
                return true;
            }
            return false;
        }

        /// <summary>A hole in the floor with nothing to climb below it: the plane flies up; anyone can drop down.</summary>
        private void Shaft(Room upper, Room lower, int floor)
        {
            const int hole = 12;
            int from = lower.X + 6, to = lower.Right - 6 - hole;
            int hx = -1;
            for (int attempt = 0; attempt < 20 && hx < 0; attempt++)
            {
                int x = rng.Range(from, Math.Max(from, to));
                if (upper.IsFree(x - 1, x + hole + 1, 0) && lower.IsFree(x - 6, x + hole + 6, 0))
                {
                    hx = x;
                }
            }
            for (int x = from; x <= to && hx < 0; x++)
            {
                if (upper.IsFree(x - 1, x + hole + 1, 0))
                {
                    hx = x;
                }
            }
            if (hx < 0)
            {
                hx = from;
            }
            bp.Clear(hx, upper.FloorTop, hole, floor);
            upper.Busy.Add((hx - 1, hx + hole + 1));
            lower.Busy.Add((hx - 8, hx + hole + 8)); // No platform under it to climb on
        }

        private void Ledges(Room upper, Room lower, int rise, int floor, int lo, int hi)
        {
            const int hole = 8;
            int count = Math.Max(1, (int)Math.Ceiling((rise - TopJump) / (double)LedgeRise));
            // A place clear of the rooms' other structures: random tries, then every column, then the least crowded
            int from = lo + 4, to = Math.Max(lo + 4, hi - hole - 4);
            int hx = -1;
            for (int attempt = 0; attempt < 14 && hx < 0; attempt++)
            {
                int x = rng.Range(from, to);
                if (lower.IsFree(x - 10, x + hole + 10) && upper.IsFree(x - 1, x + hole + 1))
                {
                    hx = x;
                }
            }
            // Then every column: the hole must never open under the upper room's own structures (that cuts them off);
            // the ledges below may stand closer to the lower room's
            for (int margin = 10; margin >= -2 && hx < 0; margin -= 2)
            {
                for (int x = from; x <= to && hx < 0; x++)
                {
                    if ((margin < 0 || lower.IsFree(x - margin, x + hole + margin, 0)) && upper.IsFree(x - 1, x + hole + 1, 0))
                    {
                        hx = x;
                    }
                }
            }
            if (hx < 0)
            {
                hx = from;
            }
            bp.Clear(hx, upper.FloorTop, hole, floor);
            upper.Busy.Add((hx - 1, hx + hole + 1));
            // The top ledge sits under the hole; lower ones alternate to the sides.
            int topLedge = Math.Max(upper.FloorTop + TopJump, lower.FloorTop - LedgeRise * count);
            bool left = rng.Chance(0.5);
            for (int i = 0; i < count; i++)
            {
                int y = topLedge + i * LedgeRise;
                if (y > lower.FloorTop - 3)
                {
                    break;
                }
                int lw = hole + 2;
                int lx = hx - 1;
                if (i > 0)
                {
                    // The side where it stands clear of the room's other structures (they would lose their headroom)
                    int a = Math.Clamp(hx - 1 + (left ? -9 : 9), lower.X + 2, lower.Right - 2 - lw);
                    int b = Math.Clamp(hx - 1 + (left ? 9 : -9), lower.X + 2, lower.Right - 2 - lw);
                    lx = lower.IsFree(a, a + lw, 1) || !lower.IsFree(b, b + lw, 1) ? a : b;
                }
                lx = Math.Clamp(lx, lower.X + 2, lower.Right - 2 - lw);
                bp.Fill(lx, y, lw, 1, Mat.Thin);
                lower.Busy.Add((lx - 3, lx + lw + 3));
                left = !left;
            }
        }

        private void PlaceStartAndExit((int C, int R) start, (int C, int R) end)
        {
            var s = rooms[start.C, start.R];
            bp.StartX = s.X + 3;
            bp.StartY = s.FloorTop - 6;
            s.Busy.Add((s.X, s.X + 14));
            var e = bp.ArenaRoom ?? rooms[end.C, end.R];
            bp.ExitX = e.Right - 10;
            bp.ExitY = e.FloorTop - 6;
            e.Busy.Add((e.Right - 14, e.Right));
        }

        // ---- Room contents -------------------------------------------------------------------------------

        private void Furnish(Room room)
        {
            if (room.Arena)
            {
                if (room == bp.ArenaRoom)
                {
                    FurnishArena(room);
                }
                return;
            }
            if (room.Hall)
            {
                if (room.Top != room.WallTop)
                {
                    int hallFirst = bp.Parts.Count;
                    FurnishHall(room);
                    furnished[room] = (hallFirst, bp.Parts.Count);
                }
                return;
            }
            var features = new (double Weight, Action<Room> Build)[]
            {
                (3.0, Platforms),
                (2.0, RaisedFloor),
                (2.0, StairsToGirder),
                (2.0, FloorBlock),
                (1.5, Hanger),
                (1.0, Partition),
                (1.4, Hazard),
            };
            var weights = features.Select(f => f.Weight).ToList();
            // A few rooms stay open; most get one to three things
            int n = rng.Weighted(new[] { 0.15, 0.35, 0.35, room.W > 44 ? 0.15 : 0.0 });
            int first = bp.Parts.Count;
            for (int k = 0; k < n; k++)
            {
                features[rng.Weighted(weights)].Build(room);
            }
            furnished[room] = (first, bp.Parts.Count);
        }

        /// <summary>
        /// Mends the layout where the robot cannot follow the main route: at the first room of the route it cannot reach,
        /// first the furniture of that room and the one before goes; then the wall between the room and a neighbour the
        /// robot does reach opens.
        /// </summary>
        public bool Repair(Func<Room, bool> reached)
        {
            for (int k = 1; k < route.Count; k++)
            {
                var room = rooms[route[k].C, route[k].R];
                if (reached(room))
                {
                    continue;
                }
                var prev = rooms[route[k - 1].C, route[k - 1].R];
                int tries = repairs.GetValueOrDefault(k);
                repairs[k] = tries + 1;
                switch (tries)
                {
                    case 0:
                        Unfurnish(prev);
                        Unfurnish(room);
                        return true;
                    case 1:
                        if (prev.Row == room.Row)
                        {
                            OpenWall(Math.Min(prev.Col, room.Col), room.Row);
                            return true;
                        }
                        foreach (int dc in new[] { -1, 1 })
                        {
                            int nc = room.Col + dc;
                            if (nc >= 0 && nc < cols && reached(rooms[nc, room.Row]))
                            {
                                OpenWall(Math.Min(nc, room.Col), room.Row);
                                return true;
                            }
                        }
                        foreach (int dc in new[] { -1, 1 })
                        {
                            int nc = prev.Col + dc;
                            if (nc >= 0 && nc < cols && !reached(rooms[nc, prev.Row]))
                            {
                                OpenWall(Math.Min(nc, prev.Col), prev.Row);
                                return true;
                            }
                        }
                        return false;
                    default:
                        return false;
                }
            }
            // The whole route is reached but not the exit: clear the last room
            var last = bp.ArenaRoom ?? rooms[route[^1].C, route[^1].R];
            if (!repairs.ContainsKey(-1))
            {
                repairs[-1] = 1;
                Unfurnish(last);
                return true;
            }
            return false;
        }

        private void Unfurnish(Room room)
        {
            if (!furnished.TryGetValue(room, out var parts))
            {
                return;
            }
            for (int i = 0; i < bp.Cells.Length; i++)
            {
                int owner = bp.Owner[i];
                if (owner >= parts.From && owner < parts.To)
                {
                    bp.Cells[i] = Mat.Air;
                    bp.Owner[i] = 0;
                }
            }
            furnished.Remove(room);
        }

        /// <summary>Opens the boundary between (c, r) and (c + 1, r), with steps between different floors.</summary>
        private void OpenWall(int c, int r)
        {
            var a = rooms[c, r];
            var b = rooms[c + 1, r];
            int hi = Math.Min(a.FloorTop, b.FloorTop), lo = Math.Max(a.FloorTop, b.FloorTop);
            int top = Math.Min(a.Top, b.Top);
            bp.Clear(b.X - 3, top, 6, hi - top);
            bool aLower = a.FloorTop > b.FloorTop;
            for (int j = 0; j < lo - hi; j++)
            {
                int bx = aLower ? b.X - 3 - 2 * j - 2 : b.X + 3 + 2 * j;
                bp.Fill(bx, hi + j, 2, 2, Mat.Step, overAir: true);
            }
            // The higher floor reaches the stairs
            if (aLower)
            {
                bp.Fill(b.X - 3, hi, 3, 2, Mat.Beam, overAir: true);
            }
            else
            {
                bp.Fill(b.X, hi, 3, 2, Mat.Beam, overAir: true);
            }
            east[c, r] = Link.Open;
        }

        /// <summary>A free column range of the given width inside the room, or -1.</summary>
        private int FreeSpot(Room room, int width, int margin = 2)
        {
            int lo = room.X + 4, hi = room.Right - 4 - width;
            if (hi < lo)
            {
                return -1;
            }
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int x = rng.Range(lo, hi);
                if (room.IsFree(x - margin, x + width + margin))
                {
                    return x;
                }
            }
            return -1;
        }

        /// <summary>Floating platforms: walk under them, jump onto them; a second tier in tall rooms.</summary>
        private void Platforms(Room room)
        {
            int clear = room.FloorTop - room.Top;
            int len = rng.Range(8, Math.Min(18, room.W - 12));
            int x = FreeSpot(room, len);
            if (x < 0)
            {
                return;
            }
            bool thin = clear < 14 || rng.Chance(0.4);
            int y = room.FloorTop - (thin ? LedgeRise : LedgeRise + 1);
            if (!bp.AreaIs(x - 1, y - 7, len + 2, 9, Mat.Air))
            {
                return;
            }
            bp.Fill(x, y, len, thin ? 1 : 2, thin ? Mat.Thin : Mat.Beam);
            room.Busy.Add((x, x + len));
            if (y - room.Top >= 15)
            {
                int len2 = rng.Range(6, 12);
                int x2 = rng.Chance(0.5) ? x - len2 + 3 : x + len - 3;
                int y2 = y - LedgeRise;
                if (x2 > room.X + 3 && x2 + len2 < room.Right - 3 && bp.AreaIs(x2 - 1, y2 - 7, len2 + 2, 8, Mat.Air))
                {
                    bp.Fill(x2, y2, len2, 1, Mat.Thin);
                }
            }
        }

        /// <summary>A raised part of the floor with steps up at one or both ends.</summary>
        private void RaisedFloor(Room room)
        {
            int clear = room.FloorTop - room.Top;
            // Usually a low raised floor; now and then a tower, its stairs climbing to the top
            bool tower = clear >= 16 && rng.Chance(0.3);
            int h = tower ? rng.Range(6, Math.Min(9, clear - 8)) : rng.Range(2, Math.Min(5, clear - 9));
            if (h < 2)
            {
                return;
            }
            int len = tower ? rng.Range(5, 9) : rng.Range(8, Math.Min(22, room.W - 14));
            bool leftSteps = rng.Chance(0.8), rightSteps = !leftSteps || rng.Chance(0.6);
            int footprint = len + (leftSteps ? 2 * h : 0) + (rightSteps ? 2 * h : 0);
            int x0 = FreeSpot(room, footprint, 3);
            if (x0 < 0 || !bp.AreaIs(x0, room.FloorTop - h - 7, footprint, h + 7, Mat.Air))
            {
                return;
            }
            int x = x0 + (leftSteps ? 2 * h : 0);
            bp.Fill(x, room.FloorTop - h, len, h, Mat.Mass);
            for (int j = 0; j < h - 1; j++)
            {
                int top = room.FloorTop - h + 1 + j;
                if (leftSteps)
                {
                    bp.Fill(x - 2 * j - 2, top, 2, 2, Mat.Step, overAir: true);
                }
                if (rightSteps)
                {
                    bp.Fill(x + len + 2 * j, top, 2, 2, Mat.Step, overAir: true);
                }
            }
            room.Busy.Add((x0, x0 + footprint));
        }

        private void StairsToGirder(Room room)
        {
            int clear = room.FloorTop - room.Top;
            int rise = rng.Range(4, Math.Min(7, clear - 8));
            if (rise < 4)
            {
                return;
            }
            int len = rng.Range(6, 14);
            int footprint = 2 * rise + len;
            int x0 = FreeSpot(room, footprint, 3);
            if (x0 < 0 || !bp.AreaIs(x0, room.FloorTop - rise - 7, footprint, rise + 7, Mat.Air))
            {
                return;
            }
            bool right = rng.Chance(0.5);
            for (int i = 0; i < rise; i++)
            {
                int bx = right ? x0 + 2 * i : x0 + footprint - 2 - 2 * i;
                bp.Fill(bx, room.FloorTop - 1 - i, 2, 2, Mat.Step, overAir: true);
            }
            int gx = right ? x0 + 2 * rise : x0;
            bp.Fill(gx, room.FloorTop - rise, len, 2, Mat.Beam);
            room.Busy.Add((x0, x0 + footprint));
        }

        /// <summary>A low block to jump over (or a pile of destructible blocks).</summary>
        private void FloorBlock(Room room)
        {
            int bw = rng.Range(3, 7), bh = rng.Range(2, 4);
            int x = FreeSpot(room, bw, 4);
            if (x < 0 || !bp.AreaIs(x, room.FloorTop - bh - 7, bw, bh + 7, Mat.Air))
            {
                return;
            }
            bp.Fill(x, room.FloorTop - bh, bw, bh, rng.Chance(0.35) ? Mat.Shoot : Mat.Mass);
            room.Busy.Add((x - 1, x + bw + 1));
        }

        /// <summary>A block hanging from the ceiling.</summary>
        private void Hanger(Room room)
        {
            int clear = room.FloorTop - room.Top;
            if (clear < 12)
            {
                return;
            }
            int bw = rng.Range(5, 12), bh = rng.Range(2, Math.Max(2, clear - 10));
            int x = rng.Range(room.X + 4, room.Right - 4 - bw);
            if (bp.AreaIs(x - 1, room.Top, bw + 2, bh + 8, Mat.Air) && room.IsFree(x, x + bw, 0))
            {
                bp.Fill(x, room.Top, bw, bh, Mat.Mass);
            }
        }

        /// <summary>A wall from the ceiling down to a passage the robot fits under.</summary>
        private void Partition(Room room)
        {
            int clear = room.FloorTop - room.Top;
            if (clear < 13)
            {
                return;
            }
            int wt = rng.Range(2, 3);
            int x = FreeSpot(room, wt, 6);
            if (x < 0 || !bp.AreaIs(x, room.Top, wt, clear, Mat.Air))
            {
                return;
            }
            bp.Fill(x, room.Top, wt, clear - 8, Mat.Mass);
        }

        /// <summary>A floor hazard where the world has them: acid pools, conveyors or slippery floor.</summary>
        private void Hazard(Room room)
        {
            // Only what the world's own floors have (the same tiles are harmless decoration on other planets)
            int Solid(params int[] tiles) => tiles.Sum(t => style.SolidTiles.Count(t));
            var kinds = new List<Mat>();
            if (Solid(0x4c, 0x4d, 0x4e, 0x52, 0x53, 0x54) >= 15)
            {
                kinds.Add(Mat.Acid);
            }
            if (Solid(0x43, 0x44, 0x45, 0x46, 0x47, 0x48) >= 15)
            {
                kinds.Add(Mat.ConveyorLeft);
                kinds.Add(Mat.ConveyorRight);
            }
            if (Solid(0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x41, 0x42) >= 40)
            {
                kinds.Add(Mat.Slide);
            }
            if (kinds.Count == 0)
            {
                return;
            }
            var kind = rng.Pick(kinds);
            int len = kind == Mat.Acid ? rng.Range(4, 7) : rng.Range(8, 16);
            int x = FreeSpot(room, len, 3);
            // The robot has no control on slippery floor: open floor at both ends lets it slide off again
            int ends = kind == Mat.Slide ? 5 : 0;
            if (x < 0 || !bp.AreaIs(x - ends, room.FloorTop - 7, len + 2 * ends, 7, Mat.Air)
                || !bp.AreaIs(x - ends, room.FloorTop, len + 2 * ends, 1, bp.At(x, room.FloorTop)) || !bp.Solid(x, room.FloorTop))
            {
                return;
            }
            bp.Fill(x, room.FloorTop, len, kind == Mat.Acid ? 2 : 1, kind);
            room.Busy.Add((x - 1, x + len + 1));
        }

        /// <summary>A hall's girders at several heights, each with room to stand on and to pass under.</summary>
        private void FurnishHall(Room room)
        {
            int n = rng.Range(2, 4);
            for (int k = 0; k < n * 4 && n > 0; k++)
            {
                int len = rng.Range(6, 14);
                int x = rng.Range(room.X + 4, room.Right - 4 - len);
                int y = rng.Range(room.Top + 8, room.FloorTop - LedgeRise);
                if (!room.IsFree(x - 2, x + len + 2, 0) || !bp.AreaIs(x - 2, y - 7, len + 4, 15, Mat.Air))
                {
                    continue;
                }
                bool thin = rng.Chance(0.5);
                bp.Fill(x, y, len, thin ? 1 : 2, thin ? Mat.Thin : Mat.Beam);
                n--;
            }
        }

        private void FurnishArena(Room room)
        {
            // Two side platforms to fight from, out of the boss's way
            int y = room.FloorTop - LedgeRise;
            int len = Math.Min(10, room.W / 5);
            bp.Fill(room.X + 3, y, len, 1, Mat.Thin);
            bp.Fill(room.Right - 3 - len - 10, y - LedgeRise, len, 1, Mat.Thin);
        }
    }
}
