namespace Thexder.Feature.Procedural
{
    /// <summary>
    /// Background decoration (passable tiles drawn behind the robot, cell flag 0x80): panels hanging under girders,
    /// support columns from a girder down to the floor, and scaffolding in some open rooms - the motifs the classic
    /// levels are full of.
    /// </summary>
    internal sealed class Decorator
    {
        private readonly Blueprint bp;
        private readonly WorldStyle style;
        private readonly ProcRandom rng;
        private readonly byte[] cells;

        public Decorator(Blueprint bp, WorldStyle style, ProcRandom rng, byte[] cells)
        {
            this.bp = bp;
            this.style = style;
            this.rng = rng;
            this.cells = cells;
        }

        private bool PanelTwo => style.UsesBackground(122) && style.UsesBackground(134);

        private bool PanelOne => style.UsesBackground(125);

        private bool Girder => style.UsesBackground(104) && style.UsesBackground(92);

        private bool Braces => style.UsesBackground(111) && style.UsesBackground(112) && style.UsesBackground(110);

        private bool Brackets => style.UsesBackground(94) && style.UsesBackground(93) && style.UsesBackground(95);

        private bool Pipe => style.UsesBackground(137) && style.UsesBackground(136);

        public void Apply()
        {
            foreach (var part in bp.Parts.ToList())
            {
                if (part.Kind is Mat.Beam or Mat.Thin && part.W >= 6)
                {
                    UnderGirder(part);
                }
            }
            foreach (var room in bp.Rooms)
            {
                if (!room.Arena && Girder && Braces && rng.Chance(0.25))
                {
                    Scaffold(room);
                }
            }
        }

        // Free air, and not where the exit portal or the robot's start is drawn
        private bool Free(int x, int y) => bp.In(x, y) && bp.At(x, y) == Mat.Air && cells[(y * bp.W + x) * 4] == 0
            && !(x >= bp.ExitX - 1 && x < bp.ExitX + 7 && y >= bp.ExitY - 1 && y < bp.ExitY + 6)
            && !(x >= bp.StartX && x < bp.StartX + 4 && y >= bp.StartY && y < bp.StartY + 6);

        private void Put(int x, int y, int tile)
        {
            if (Free(x, y))
            {
                int i = (y * bp.W + x) * 4;
                cells[i] = (byte)tile;
                cells[i + 3] |= 0x80;
            }
        }

        /// <summary>Under a girder: a panel along it, and columns from its ends down to the floor.</summary>
        private void UnderGirder(Part part)
        {
            int below = part.Y + part.H;
            // Only under a girder's full length of air
            int x0 = part.X, x1 = part.X + part.W - 1;
            while (x0 <= x1 && !Free(x0, below))
            {
                x0++;
            }
            while (x1 >= x0 && !Free(x1, below))
            {
                x1--;
            }
            if (x1 - x0 < 4)
            {
                return;
            }
            int y = below;
            double roll = rng.NextDouble();
            if (PanelTwo && roll < 0.45 && RowFree(x0, x1, y) && RowFree(x0, x1, y + 1))
            {
                Row(x0, x1, y, 121, 122, 123);
                Row(x0, x1, y + 1, 133, 134, 135);
                y += 2;
            }
            else if (PanelOne && roll < 0.7 && RowFree(x0, x1, y))
            {
                Row(x0, x1, y, 124, 125, 126);
                y += 1;
            }
            // Supports at the ends and every 10-14 cells, where a floor is close enough below
            var columns = new List<int> { x0 + 1, x1 - 1 };
            for (int cx = x0 + rng.Range(10, 14); cx < x1 - 6; cx += rng.Range(10, 14))
            {
                columns.Add(cx);
            }
            bool pipe = Pipe && (!Girder || rng.Chance(0.4));
            if (!Girder && !pipe)
            {
                return;
            }
            foreach (int cx in columns)
            {
                int floor = y;
                while (floor < bp.H && Free(cx, floor) && floor - y < 24)
                {
                    floor++;
                }
                if (!bp.Solid(cx, floor) || floor - y < 3)
                {
                    continue;
                }
                if (!pipe && Brackets && Free(cx - 1, y) && Free(cx + 1, y) && rng.Chance(0.4))
                {
                    Put(cx - 1, y, 93);
                    Put(cx, y, 94);
                    Put(cx + 1, y, 95);
                    Column(cx, y + 1, floor, false);
                }
                else
                {
                    Column(cx, y, floor, pipe);
                }
            }
        }

        private bool RowFree(int x0, int x1, int y)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (!Free(x, y))
                {
                    return false;
                }
            }
            return true;
        }

        private void Row(int x0, int x1, int y, int left, int mid, int right)
        {
            for (int x = x0; x <= x1; x++)
            {
                Put(x, y, x == x0 ? left : x == x1 ? right : mid);
            }
        }

        private void Column(int x, int from, int to, bool pipe)
        {
            for (int y = from; y < to; y++)
            {
                Put(x, y, pipe ? (y == from ? 136 : 137) : y == from ? 92 : (y - from) % 7 == 6 ? 110 : 104);
            }
        }

        /// <summary>Scaffolding across an open room: columns floor to ceiling joined by braces.</summary>
        private void Scaffold(Room room)
        {
            int left = room.X + rng.Range(3, 8), right = room.Right - rng.Range(3, 8);
            int step = rng.Range(8, 12);
            var columns = new List<int>();
            for (int x = left; x < right; x += step)
            {
                columns.Add(x);
            }
            if (columns.Count < 2)
            {
                return;
            }
            int top = room.Top, bottom = room.FloorTop;
            int braceGap = rng.Range(6, 8);
            for (int k = 0; k < columns.Count; k++)
            {
                int cx = columns[k];
                for (int y = top; y < bottom; y++)
                {
                    bool brace = (y - top) % braceGap == braceGap - 1;
                    Put(cx, y, brace ? 110 : 104);
                    if (brace && k + 1 < columns.Count)
                    {
                        for (int bx = cx + 1; bx < columns[k + 1]; bx++)
                        {
                            Put(bx, y, bx == cx + 1 ? 111 : 112);
                        }
                    }
                }
            }
        }
    }
}
