namespace Thexder.Feature.Procedural
{
    /// <summary>All tile sheets share one layout, so the same numbers give each planet its own look.</summary>
    internal sealed class Skinner
    {
        // Tiles that may fill the inside of a thick block, and those that make girder tops and single-row platforms.
        private static readonly HashSet<int> FillTiles = new() { 2, 14, 246, 234, 240, 3, 4, 1 };
        private static readonly HashSet<int> TopTiles = new() { 8, 11, 23, 2, 14 };
        private static readonly HashSet<int> ThinTiles = new() { 11, 8, 20, 17 };

        private readonly Blueprint bp;
        private readonly WorldStyle style;
        private readonly ClassicLibrary lib;
        private readonly ProcRandom rng;
        private readonly Dictionary<(int Part, int Role), int> chosen = new();

        public Skinner(Blueprint bp, WorldStyle style, ClassicLibrary lib, ProcRandom rng)
        {
            this.bp = bp;
            this.style = style;
            this.lib = lib;
            this.rng = rng;
        }

        public void Apply(byte[] cells)
        {
            int beamTop = PickTop(-1);
            int beamBottom = BottomFor(beamTop);
            int shoot = style.ShootTiles.IsEmpty ? 138 : style.ShootTiles.Count(138) > 0 ? 138 : style.ShootTiles.Top(138);
            for (int y = 0; y < bp.H; y++)
            {
                for (int x = 0; x < bp.W; x++)
                {
                    int i = y * bp.W + x;
                    var mat = bp.Cells[i];
                    if (mat == Mat.Air)
                    {
                        continue;
                    }
                    var part = bp.Parts[bp.Owner[i]];
                    int tile;
                    switch (mat)
                    {
                        case Mat.Beam:
                            tile = BeamTile(part, x, y);
                            break;
                        case Mat.Thin:
                            tile = ThinTile(part, x, y);
                            break;
                        case Mat.Step:
                        {
                            bool top = ((y - part.Y) & 1) == 0, left = ((x - part.X) & 1) == 0;
                            int run = top ? beamTop : beamBottom;
                            tile = left ? Cap(style.LeftCap, run, run) : Cap(style.RightCap, run, run);
                            break;
                        }
                        case Mat.Shoot:
                            tile = shoot;
                            cells[i * 4 + 2] |= 0x80;
                            break;
                        case Mat.Acid:
                            tile = (y == part.Y ? 76 : 82) + x % 3;
                            break;
                        case Mat.ConveyorRight:
                        case Mat.ConveyorLeft:
                        {
                            int b = mat == Mat.ConveyorRight ? 67 : 70;
                            tile = bp.At(x - 1, y) != mat ? b : bp.At(x + 1, y) != mat ? b + 2 : b + 1;
                            break;
                        }
                        case Mat.Slide:
                            tile = bp.At(x - 1, y) != mat ? 59 : bp.At(x + 1, y) != mat ? 66 : 62;
                            break;
                        default:
                            tile = MassTile(part, x, y);
                            break;
                    }
                    cells[i * 4] = (byte)tile;
                }
            }
        }

        private int ShapeAt(int x, int y) => ClassicLibrary.ShapeOf(bp.Solid, x, y);

        private TileOdds Odds(int shape) => style.Shape[shape].IsEmpty ? lib.GlobalShape[shape] : style.Shape[shape];

        /// <summary>A choice made once per structure and role, so a wall keeps the same edge all along.</summary>
        private int Once(int part, int role, Func<int> choose)
        {
            if (!chosen.TryGetValue((part, role), out int tile))
            {
                tile = choose();
                chosen[(part, role)] = tile;
            }
            return tile;
        }

        private int PickFrom(TileOdds odds, HashSet<int> allowed, int fallback)
        {
            var filtered = new TileOdds();
            foreach (int t in allowed)
            {
                int n = odds.Count(t);
                if (n > 0)
                {
                    filtered.Add(t, n);
                }
            }
            return filtered.IsEmpty ? fallback : filtered.Pick(rng, fallback);
        }

        private static int Top(TileOdds odds, HashSet<int> allowed, int fallback)
        {
            int best = fallback, most = 0;
            foreach (int t in allowed.OrderBy(t => t))
            {
                if (odds.Count(t) > most)
                {
                    most = odds.Count(t);
                    best = t;
                }
            }
            return best;
        }

        private int PickTop(int part) => Once(part, 100, () => PickFrom(Odds(0b0111), TopTiles, 8));

        private int BottomFor(int top) =>
            style.Below.TryGetValue(top, out var odds) && !odds.IsEmpty ? odds.Top(20) : Odds(0b1101).Top(20);

        private static int Cap(Dictionary<int, TileOdds> caps, int run, int fallback) =>
            caps.TryGetValue(run, out var odds) && !odds.IsEmpty ? odds.Top(fallback) : fallback;

        private int BeamTile(Part part, int x, int y)
        {
            int top = PickTop(part.Id);
            int bottom = Once(part.Id, 101, () => BottomFor(top));
            bool upper = y == part.Y || bp.At(x, y - 1) == Mat.Air;
            int run = upper ? top : bottom;
            bool openLeft = !bp.Solid(x - 1, y), openRight = !bp.Solid(x + 1, y);
            if (openLeft && !openRight)
            {
                return Cap(style.LeftCap, run, run);
            }
            if (openRight && !openLeft)
            {
                return Cap(style.RightCap, run, run);
            }
            return run;
        }

        private int ThinTile(Part part, int x, int y)
        {
            int run = Once(part.Id, 102, () => PickFrom(Odds(0b0101), ThinTiles, 11));
            bool openLeft = !bp.Solid(x - 1, y), openRight = !bp.Solid(x + 1, y);
            if (openLeft && !openRight)
            {
                return Cap(style.LeftCap, run, run);
            }
            if (openRight && !openLeft)
            {
                return Cap(style.RightCap, run, run);
            }
            return run;
        }

        private int MassTile(Part part, int x, int y)
        {
            int shape = ShapeAt(x, y);
            if (shape == 0b1111)
            {
                // The inside: the structure's fill, now and then a row of its second fill big masses (the borders, the
                // ground) take the world's main fill; walls and blocks vary
                bool big = part.W * part.H > 400;
                int fill = Once(part.Id, 200, () => big ? Top(Odds(15), FillTiles, 14) : PickFrom(Odds(15), FillTiles, 14));
                int alt = Once(part.Id, 201, () => big ? fill : PickFrom(Odds(15), FillTiles, fill));
                int rowMix = Once(part.Id, 1000 + y, () => rng.Chance(0.25) ? 1 : 0);
                return rowMix == 1 ? alt : fill;
            }
            if (shape == 0b0111)
            {
                return PickTop(part.Id);
            }
            if (shape == 0b1101)
            {
                return Once(part.Id, 101, () => BottomFor(PickTop(part.Id)));
            }
            if (shape == 0b0110)
            {
                return Cap(style.LeftCap, PickTop(part.Id), Odds(shape).Top(22));
            }
            if (shape == 0b0011)
            {
                return Cap(style.RightCap, PickTop(part.Id), Odds(shape).Top(24));
            }
            int bottomRun = Once(part.Id, 101, () => BottomFor(PickTop(part.Id)));
            if (shape == 0b1100)
            {
                return Cap(style.LeftCap, bottomRun, Odds(shape).Top(19));
            }
            if (shape == 0b1001)
            {
                return Cap(style.RightCap, bottomRun, Odds(shape).Top(21));
            }
            return Once(part.Id, shape, () => Odds(shape).Pick(rng, 14));
        }
    }
}
