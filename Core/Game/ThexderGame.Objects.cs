namespace Thexder.Core.Game
{
    // Level objects, platforms and the map-reveal equipment.
    internal sealed partial class ThexderGame
    {
        /// <summary>Pickups and bombs fall one cell per frame and slide off ledges.</summary>
        private void ObjectsFall()
        {
            foreach (var o in Objects)
            {
                if (o.Kind == 0)
                {
                    continue;
                }
                int idx = Map.Index(o.X, o.Y);
                if (idx < 0 || idx >= Map.Count)
                {
                    continue;
                }
                byte b3 = Map.B3(idx), b1 = Map.B1(idx);
                if (o.Kind == LevelObject.KindPickup)
                {
                    Map.SetB3(idx, (byte)(b3 & 0xc0)); // Lift the marker
                }
                var (nx, ny) = MoveBox(o.X, o.Y, 8, 1, 2, 2, checkEnemy: false, raw: false);
                if (ny == o.Y || (Map.B3(Map.Index(nx, ny)) & 0x3f) != 0)
                {
                    var (lx, ly) = ObjectDiagonal(o.X, o.Y, -2);
                    bool canL = lx != o.X && ly != o.Y && (Map.B3(Map.Index(lx, ly)) & 0x3f) == 0;
                    var (rx, ry) = ObjectDiagonal(o.X, o.Y, 2);
                    bool canR = rx != o.X && ry != o.Y && (Map.B3(Map.Index(rx, ry)) & 0x3f) == 0;
                    if (canL && canR)
                    {
                        (nx, ny) = Random.Next() < 15000 ? (lx, ly) : (rx, ry);
                    }
                    else if (canL)
                    {
                        (nx, ny) = (lx, ly);
                    }
                    else if (canR)
                    {
                        (nx, ny) = (rx, ry);
                    }
                    else
                    {
                        (nx, ny) = (o.X, o.Y);
                    }
                }
                if (o.Kind == LevelObject.KindPickup)
                {
                    int d = Map.Index(nx, ny);
                    Map.SetB3(d, (byte)((Map.B3(d) & 0xc0) | (b3 & 0x3f)));
                    Map.SetB1(d, (byte)((Map.B1(d) & 0x80) | (b1 & 0x7f)));
                    if (nx != o.X || ny != o.Y)
                    {
                        MapPlot(o.X, o.Y, 2, 2, 0);
                    }
                }
                o.X = nx;
                o.Y = ny;
            }
        }

        private (int X, int Y) ObjectDiagonal(int x, int y, int side)
        {
            int nx = x + side, ny = y + 2;
            if (side < 0 ? (nx < 1 || nx + 2 > MapW - 1 || ny + 2 > MapH - 1) : (nx == 0 || nx + 2 > MapW - 1 || ny + 2 > MapH - 1))
            {
                return (x, y);
            }
            for (int row = ny; row < ny + 2; row++)
            {
                for (int col = nx; col < nx + 2; col++)
                {
                    int idx = row * MapW + col;
                    if (Map.IsSolid(idx) || (Map.B3(idx) & 0x3f) != 0 || (Map.B2(idx) & 0x7f) != 0)
                    {
                        return (x, y);
                    }
                }
            }
            return (nx, ny);
        }

        /// <summary>Moves platforms, carries and pushes the player.</summary>
        private void UpdatePlatforms()
        {
            var p = Player;
            p.MoveEnable = 1;
            int px = p.X, pw = FormW[p.Form], ph = FormH[p.Form];
            if ((p.StateFlags & PlayerState.FlagTransforming) != 0)
            {
                int i = p.Form * 3 + p.TransformTarget;
                pw = TransformW[i];
                ph = TransformH[i];
            }
            if (p.Form == PlayerState.Walker && (p.StateFlags & PlayerState.FlagTransforming) == 0)
            {
                px++;
                pw -= 2;
            }
            int carrier = -1;
            for (int i = 0; i < Platforms.Length; i++)
            {
                var pl = Platforms[i];
                if (pl.State == 0 || (pl.Attacking != 0 && (pl.Flags & EnemyRecord.PlatformSwitchedOn) == 0))
                {
                    continue;
                }
                int ox = pl.X, oy = pl.Y;
                pl.AnimTick = (pl.AnimTick + 1) & 0xffff;
                if (pl.Speed == 1 && (pl.AnimTick & 1) == 0)
                {
                    continue; // Speed 1: every other frame
                }
                if (PhysicsOn && p.Y + ph == pl.Y && px < pl.X + pl.W && pl.X < px + pw)
                {
                    carrier = i;
                }
                MovePlatform(pl);
                if (carrier == i && pl.X == ox && pl.Y == oy)
                {
                    carrier = -1;
                }
            }
            if (carrier >= 0)
            {
                var pl = Platforms[carrier];
                int n = pl.Speed;
                if (n != 1)
                {
                    n--;
                }
                PushPlayer(pl.Dir, n);
            }

            foreach (var pl in Platforms)
            {
                if (pl.State == 0 || !PhysicsOn)
                {
                    continue;
                }
                var box = PlayerBodyBox();
                var pr = new Engine.Rect(pl.X, pl.Y, pl.W, pl.H);
                var ov = box.Intersect(pr);
                if (ov.IsEmpty)
                {
                    continue;
                }
                if (pl.Mode == 7)
                {
                    PushPlayer(pl.Dir, ov.Width);
                }
                else if (pl.Mode == 8)
                {
                    PushPlayer(pl.Dir, ov.Height);
                }
                else
                {
                    PushPlayer(p.X < pl.X ? 1 : 2, ov.Height);
                    if (!PlayerBodyBox().Intersect(pr).IsEmpty)
                    {
                        PushPlayer(p.Y < pl.Y ? 8 : 4, ov.Height); // Kept literally: above -> down
                    }
                }
                if (!PlayerBodyBox().Intersect(pr).IsEmpty)
                {
                    DamagePlayer(10, false);
                    Sounds.Play(0x16);
                    pl.Dir = ReverseDir(pl.Dir);
                    MovePlatform(pl);
                }
            }
        }

        private Engine.Rect PlayerBodyBox()
        {
            var p = Player;
            int x = p.X, w = FormW[p.Form], h = FormH[p.Form];
            if ((p.StateFlags & PlayerState.FlagTransforming) != 0)
            {
                int i = p.Form * 3 + p.TransformTarget;
                w = TransformW[i];
                h = TransformH[i];
            }
            if (p.Form == PlayerState.Walker && (p.StateFlags & PlayerState.FlagTransforming) == 0)
            {
                x++;
                w -= 2;
            }
            return new Engine.Rect(x, p.Y, w, h);
        }

        /// <summary>Lifts the platform's tiles, moves it with the enemy mover, puts them back.</summary>
        private void MovePlatform(EnemyRecord pl)
        {
            int count = pl.W * pl.H;
            var tiles = new byte[Math.Min(count, 0x500)];
            var seen = new byte[tiles.Length];
            for (int r = 0; r < pl.H; r++)
            {
                for (int c = 0; c < pl.W; c++)
                {
                    int k = pl.W * r + c;
                    if (k >= 0x500)
                    {
                        continue;
                    }
                    int idx = (pl.Y + r) * MapW + pl.X + c;
                    if (idx < 0 || idx >= Map.Count)
                    {
                        continue;
                    }
                    tiles[k] = Map.Tile(idx);
                    seen[k] = (byte)(Map.B1(idx) & 0x80);
                    Map.SetTile(idx, 0);
                    Map.SetB1(idx, (byte)(Map.B1(idx) & 0x7f));
                    MapSurface.Pixels[idx] = 0;
                }
            }
            int nx = pl.X, ny = pl.Y, dir = pl.Dir, frame = pl.Frame;
            EnemyAI(pl, ref nx, ref ny, ref dir, ref frame);
            pl.X = nx;
            pl.Y = ny;
            pl.Dir = dir;
            for (int r = 0; r < pl.H; r++)
            {
                for (int c = 0; c < pl.W; c++)
                {
                    int k = pl.W * r + c;
                    int idx = (ny + r) * MapW + nx + c;
                    if (k < 0x500 && idx >= 0 && idx < Map.Count && Map.Tile(idx) == 0)
                    {
                        Map.SetTile(idx, tiles[k]);
                        Map.SetB1(idx, (byte)((Map.B1(idx) & 0x7f) | seen[k]));
                        if ((Map.B1(idx) & 0x80) != 0)
                        {
                            MapSurface.Pixels[idx] = 0x45;
                        }
                    }
                }
            }
        }

        // ---- Map-reveal equipment ------------------------------------------------------------------------

        /// <summary>ItemEffect: View-Objects / View-Cells / View-Shoot Away / View-Foreground on the planet map.</summary>
        private void ItemEffect(int item, bool on)
        {
            switch (item)
            {
                case ItemType.ViewObjects:
                    MapPlot(Header.ExitX, Header.ExitY, 6, 6, (byte)(on ? 0x73 : 0));
                    for (int i = 0; i < Map.Count; i++)
                    {
                        if ((Map.B3(i) & 0x3f) != 0)
                        {
                            MapPlot(i % MapW, i / MapW, 2, 2, (byte)(on ? 0x10 : 0));
                        }
                    }
                    ViewObjectsActive = on;
                    for (int k = 1; k < Enemies.Length; k++)
                    {
                        var t = Enemies[k];
                        if (t.State != 0 && t.State != EnemyRecord.Remains)
                        {
                            MapPlot(t.X, t.Y, t.W, t.H, (byte)(on ? 0x26 : 0));
                            t.Flags = on ? t.Flags | 1 : t.Flags & ~1;
                        }
                    }
                    break;
                case ItemType.ViewCells:
                    for (int i = 0; i < Map.Count; i++)
                    {
                        if (Map.IsSolid(i))
                        {
                            if (on)
                            {
                                MapSurface.Pixels[i] = 0x45;
                            }
                            else if ((Map.B1(i) & 0x80) == 0)
                            {
                                MapSurface.Pixels[i] = 0;
                            }
                        }
                    }
                    break;
                case ItemType.ViewShootAway:
                    RevealCells(on, 0x35, i => (Map.B2(i) & 0x80) != 0);
                    break;
                case ItemType.ViewForeground:
                    RevealCells(on, 0x81, i => (Map.B3(i) & 0x40) != 0);
                    break;
            }
        }

        private void RevealCells(bool on, byte color, Func<int, bool> match)
        {
            for (int i = 0; i < Map.Count; i++)
            {
                if (Map.Tile(i) != 0 && match(i))
                {
                    MapSurface.Pixels[i] = on ? color : (Map.B1(i) & 0x80) != 0 ? (byte)0x45 : (byte)0;
                }
            }
        }

        private void RebuildMapFromSeen()
        {
            MapSurface.Clear();
            for (int i = 0; i < Map.Count; i++)
            {
                if ((Map.B1(i) & 0x80) != 0)
                {
                    MapSurface.Pixels[i] = 0x45;
                }
            }
        }
    }
}
