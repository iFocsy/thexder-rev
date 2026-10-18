namespace Thexder.Feature.Procedural
{
    /// <summary>What a cell of a level being generated is made of, before it gets its tile.</summary>
    internal enum Mat : byte
    {
        Air,
        /// <summary>Thick solid: borders, walls, blocks.</summary>
        Mass,
        /// <summary>A 2-row girder or floor (top tile over bottom tile).</summary>
        Beam,
        /// <summary>A 1-row platform.</summary>
        Thin,
        /// <summary>A 2 x 2 stair block.</summary>
        Step,
        /// <summary>Destructible ("shoot-away") block.</summary>
        Shoot,
        Acid,
        ConveyorLeft,
        ConveyorRight,
        Slide,
    }

    /// <summary>
    /// One structure of the blueprint (a wall, a floor, a platform...): its cells share their tile choices.
    /// </summary>
    internal sealed record Part(int Id, Mat Kind, int X, int Y, int W, int H);

    /// <summary>A room of the layout grid: its column and row, its air span and its floor.</summary>
    internal sealed class Room
    {
        public int Col { get; init; }

        public int Row { get; init; }

        /// <summary>First column of the room (its left wall, when it has one, stands on X - 1..</summary>
        public int X { get; init; }

        public int W { get; init; }

        /// <summary>First air row under the ceiling.</summary>
        public int Top { get; set; }

        /// <summary>
        /// The row's own first air row: walls between rooms keep to it when a hall or the arena takes in the room above.
        /// </summary>
        public int WallTop { get; init; }

        /// <summary>Part of a tall hall (this room and the one above or below it, with no floor between).</summary>
        public bool Hall { get; set; }

        /// <summary>The floor's top row (the row the robot stands on).</summary>
        public int FloorTop { get; set; }

        public bool OnPath { get; set; }

        /// <summary>Reached only through a destructible wall: a store of pickups.</summary>
        public bool Vault { get; set; }

        /// <summary>The boss arena (two rows tall).</summary>
        public bool Arena { get; set; }

        /// <summary>
        /// Column ranges already used by stairs, ledges, holes and the start or exit, so features keep clear.
        /// </summary>
        public List<(int From, int To)> Busy { get; } = new();

        public int Right => X + W;

        public bool IsFree(int from, int to, int margin = 2) => Busy.All(b => to + margin <= b.From || from >= b.To + margin);
    }

    /// <summary>The level as materials, rooms and structures; the skin and the decorations turn it into tiles.</summary>
    internal sealed class Blueprint
    {
        public Blueprint(int width, int height)
        {
            W = width;
            H = height;
            Cells = new Mat[width * height];
            Owner = new int[width * height];
        }

        public int W { get; }

        public int H { get; }

        public Mat[] Cells { get; }

        /// <summary>The <see cref="Part"/> id each cell belongs to (0 for none).</summary>
        public int[] Owner { get; }

        public List<Part> Parts { get; } = new() { new Part(0, Mat.Air, 0, 0, 0, 0) };

        public Room[,] Rooms { get; set; } = new Room[0, 0];

        public int StartX { get; set; }

        public int StartY { get; set; }

        public int ExitX { get; set; }

        public int ExitY { get; set; }

        public Room? ArenaRoom { get; set; }

        public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

        public Mat At(int x, int y) => In(x, y) ? Cells[y * W + x] : Mat.Mass;

        /// <summary>Solid for the robot (everything but air).</summary>
        public bool Solid(int x, int y) => At(x, y) != Mat.Air;

        /// <summary>Fills a rectangle with a material as one new structure (clipped to the map); returns it.</summary>
        public Part Fill(int x, int y, int w, int h, Mat kind, bool overAir = false)
        {
            var part = new Part(Parts.Count, kind, x, y, w, h);
            Parts.Add(part);
            for (int cy = Math.Max(0, y); cy < Math.Min(H, y + h); cy++)
            {
                for (int cx = Math.Max(0, x); cx < Math.Min(W, x + w); cx++)
                {
                    int i = cy * W + cx;
                    if (overAir && Cells[i] != Mat.Air)
                    {
                        continue;
                    }
                    Cells[i] = kind;
                    Owner[i] = part.Id;
                }
            }
            return part;
        }

        public void Clear(int x, int y, int w, int h)
        {
            for (int cy = Math.Max(0, y); cy < Math.Min(H, y + h); cy++)
            {
                for (int cx = Math.Max(0, x); cx < Math.Min(W, x + w); cx++)
                {
                    Cells[cy * W + cx] = Mat.Air;
                    Owner[cy * W + cx] = 0;
                }
            }
        }

        public bool AreaIs(int x, int y, int w, int h, Mat kind)
        {
            for (int cy = y; cy < y + h; cy++)
            {
                for (int cx = x; cx < x + w; cx++)
                {
                    if (At(cx, cy) != kind)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
