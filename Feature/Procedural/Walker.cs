namespace Thexder.Feature.Procedural
{
    /// <summary>
    /// Where the robot can get to on a finished level, by its own rules: <list type="bullet"> <item>walker: a body two
    /// cells wide and six tall (its collision box), climbing one-cell steps, falling, jumping up to ten cells while
    /// drifting sideways;</item> <item>plane (when <see cref="Plane"/>): a 5 x 5 box flying freely in eight
    /// directions;</item> <item>tank (when <see cref="Tank"/>): a 4 x 3 box that falls, climbs one-cell steps and drills
    /// through destructible blocks;</item> <item>a change of form wherever a free 5 x 6 area is at hand (the game also
    /// tries places up to three cells away), the tank settling at the bottom of it.</item> </list> The generator uses it
    /// to prove the exit can be reached with the forms the player has, and to put pickups only where the walking robot
    /// can collect them.
    /// </summary>
    internal sealed class Walker
    {
        private const int Rise = 10;
        private const int Drift = 12;

        // RequestTransform's places to try when the robot's own place is too tight
        private static readonly int[] TransformDx = { 0, 0, 0, 0, -1, -2, -3, 1, 2, 3, 1, 2, -1, -2, -3 };
        private static readonly int[] TransformDy = { 0, -1, -2, -3, -1, -2, -3, -1, -2, -3, 0, 0, 0, 0, 0 };

        private readonly int w, h;
        private readonly byte[] cells;
        private readonly bool[] seen;
        private readonly int[] solidSum; // Summed-area table of solid cells, (w + 1) x (h + 1)
        private readonly int[] hardSum; // The same without destructible cells (what the tank cannot drill)

        public Walker(int width, int height, byte[] cells)
        {
            w = width;
            h = height;
            this.cells = cells;
            seen = new bool[3 * width * height];
            solidSum = new int[(width + 1) * (height + 1)];
            hardSum = new int[(width + 1) * (height + 1)];
            for (int y = 0; y < height; y++)
            {
                int row = 0, hardRow = 0;
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    bool solid = cells[i] != 0 && (cells[i + 3] & 0xc0) == 0;
                    row += solid ? 1 : 0;
                    hardRow += solid && (cells[i + 2] & 0x80) == 0 ? 1 : 0;
                    solidSum[(y + 1) * (width + 1) + x + 1] = solidSum[y * (width + 1) + x + 1] + row;
                    hardSum[(y + 1) * (width + 1) + x + 1] = hardSum[y * (width + 1) + x + 1] + hardRow;
                }
            }
        }

        /// <summary>The plane can be used (from the campaign's second world).</summary>
        public bool Plane { get; init; }

        /// <summary>The tank can be used (from the third world).</summary>
        public bool Tank { get; init; }

        /// <summary>The walker's standing positions found (its top-left cell).</summary>
        public List<(int X, int Y)> Stands { get; } = new();

        public bool Reached(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && seen[y * w + x];

        private bool Solid(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
            {
                return true;
            }
            int i = (y * w + x) * 4;
            return cells[i] != 0 && (cells[i + 3] & 0xc0) == 0;
        }

        // Whether a box is free of solid cells (or of undrillable ones) and inside the map.
        private bool BoxFree(int[] sum, int x, int y, int bw, int bh)
        {
            if (x < 0 || y < 0 || x + bw > w || y + bh > h)
            {
                return false;
            }
            int s = w + 1;
            return sum[(y + bh) * s + x + bw] - sum[y * s + x + bw] - sum[(y + bh) * s + x] + sum[y * s + x] == 0;
        }

        private bool Clear(int x, int y) => BoxFree(solidSum, x + 1, y, 2, 6);

        private bool Supported(int x, int y) => Solid(x + 1, y + 6) || Solid(x + 2, y + 6);

        private bool PlaneClear(int x, int y) => BoxFree(solidSum, x, y, 5, 5);

        private bool TankClear(int x, int y) => BoxFree(hardSum, x, y, 4, 3);

        private bool TankSupported(int x, int y) => !BoxFree(solidSum, x, y + 3, 4, 1) || y + 3 >= h;

        /// <summary>Falls from (x, y) until standing; -1 when it falls out of the map.</summary>
        private int Settle(int x, int y)
        {
            while (!Supported(x, y))
            {
                if (!Clear(x, y + 1))
                {
                    return y;
                }
                y++;
                if (y + 6 >= h)
                {
                    return -1;
                }
            }
            return y;
        }

        private int TankSettle(int x, int y)
        {
            while (!TankSupported(x, y))
            {
                if (!TankClear(x, y + 1))
                {
                    return y;
                }
                y++;
                if (y + 3 >= h)
                {
                    return -1;
                }
            }
            return y;
        }

        /// <summary>Explores from the start; true when the robot can touch the 6 x 6 exit with the forms it has.</summary>
        public bool Explore(int startX, int startY, int exitX, int exitY)
        {
            int sy = Settle(startX, startY);
            if (sy < 0 || !Clear(startX, sy))
            {
                return false;
            }
            var queue = new Queue<(int Form, int X, int Y)>();
            bool Touches(int x, int y, int bw, int bh) => x < exitX + 6 && exitX < x + bw && y < exitY + 6 && exitY < y + bh;
            bool TouchesAs(int form, int x, int y) => form switch
            {
                0 => Touches(x, y, 4, 6),
                1 => Touches(x, y, 5, 5),
                _ => Touches(x, y, 4, 3),
            };
            void Visit(int form, int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h)
                {
                    return;
                }
                int i = (form * h + y) * w + x;
                if (!seen[i])
                {
                    seen[i] = true;
                    if (form == 0 && !TouchesAs(0, x, y))
                    {
                        Stands.Add((x, y));
                    }
                    queue.Enqueue((form, x, y));
                }
            }
            void Transform(int x, int y, int from)
            {
                for (int k = 0; k < TransformDx.Length; k++)
                {
                    int tx = x + TransformDx[k], ty = y + TransformDy[k];
                    if (!BoxFree(solidSum, tx, ty, 5, 6))
                    {
                        continue;
                    }
                    if (from != 0)
                    {
                        int wy = Settle(tx, ty);
                        if (wy >= 0 && Clear(tx, wy))
                        {
                            Visit(0, tx, wy);
                        }
                    }
                    if (Plane && from != 1)
                    {
                        Visit(1, tx, ty);
                    }
                    if (Tank && from != 2)
                    {
                        int ky = TankSettle(tx, ty + 3);
                        if (ky >= 0)
                        {
                            Visit(2, tx, ky);
                        }
                    }
                    return;
                }
            }

            Visit(0, startX, sy);
            bool exit = false;
            while (queue.Count > 0)
            {
                var (form, x, y) = queue.Dequeue();
                if (TouchesAs(form, x, y))
                {
                    exit = true; // The level ends here
                    continue;
                }
                if (form == 0)
                {
                    WalkerMoves(x, y, Visit);
                }
                else if (form == 1)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if ((dx != 0 || dy != 0) && PlaneClear(x + dx, y + dy))
                            {
                                Visit(1, x + dx, y + dy);
                            }
                        }
                    }
                }
                else
                {
                    foreach (int dir in new[] { -1, 1 })
                    {
                        int nx = x + dir;
                        if (TankClear(nx, y))
                        {
                            int ny = TankSettle(nx, y);
                            if (ny >= 0)
                            {
                                Visit(2, nx, ny);
                            }
                        }
                        else if (TankClear(nx, y - 1) && TankClear(x, y - 1))
                        {
                            Visit(2, nx, y - 1); // A one-cell step
                        }
                    }
                }
                if (Plane || Tank)
                {
                    Transform(x, y, form);
                }
            }
            return exit;
        }

        private void WalkerMoves(int x, int y, Action<int, int, int> visit)
        {
            foreach (int dir in new[] { -1, 1 })
            {
                int nx = x + dir;
                if (Clear(nx, y))
                {
                    int ny = Settle(nx, y);
                    if (ny >= 0)
                    {
                        visit(0, nx, ny);
                    }
                }
                else if (Clear(nx, y - 1) && Clear(x, y - 1))
                {
                    visit(0, nx, y - 1); // A one-cell step
                }
            }
            // Jumps: rise as far as the ceiling allows, drifting sideways at a few heights
            int top = 0;
            while (top < Rise && Clear(x, y - top - 1))
            {
                top++;
            }
            foreach (int k in new[] { top, top * 2 / 3, top / 3 })
            {
                if (k <= 0)
                {
                    continue;
                }
                foreach (int dir in new[] { -1, 1 })
                {
                    for (int d = 1; d <= Drift; d++)
                    {
                        int nx = x + dir * d;
                        if (!Clear(nx, y - k))
                        {
                            break;
                        }
                        int ny = Settle(nx, y - k);
                        if (ny >= 0)
                        {
                            visit(0, nx, ny);
                        }
                    }
                }
            }
        }
    }
}
