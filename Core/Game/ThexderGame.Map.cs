namespace Thexder.Core.Game
{
    // Cell map queries shared by the player, the enemies, platforms and objects.
    internal sealed partial class ThexderGame
    {
        /// <summary>
        /// Solid tile (or, with <paramref name="raw"/>, any tile), OR'ed with the index of a live enemy occupying the cell
        /// when <paramref name="checkEnemy"/>.
        /// </summary>
        public int CellBlocked(int index, bool checkEnemy, bool raw)
        {
            index = Map.Wrap16(index);
            if (index >= Map.Count)
            {
                return 0;
            }
            int r = raw ? Map.Tile(index) : (Map.IsSolid(index) ? 1 : 0);
            if (checkEnemy)
            {
                int id = Map.B2(index) & 0x7f;
                if (id != 0 && id < Enemies.Length && Enemies[id].State == EnemyRecord.Alive)
                {
                    r |= id;
                }
            }
            return r;
        }

        private bool Blocked(int x, int y, bool t, bool r) => CellBlocked(y * MapW + x, t, r) != 0;

        public bool BlockedL(int x, int y, int w, int h, bool t, bool r)
        {
            if (x < 1)
            {
                return true;
            }
            for (int i = 0; i < h; i++)
            {
                if (Blocked(x - 1, y + i, t, r))
                {
                    return true;
                }
            }
            return false;
        }

        public bool BlockedR(int x, int y, int w, int h, bool t, bool r)
        {
            if (x >= MapW - w)
            {
                return true;
            }
            for (int i = 0; i < h; i++)
            {
                if (Blocked(x + w, y + i, t, r))
                {
                    return true;
                }
            }
            return false;
        }

        public bool BlockedU(int x, int y, int w, int h, bool t, bool r)
        {
            if (y < 1)
            {
                return true;
            }
            for (int i = 0; i < w; i++)
            {
                if (Blocked(x + i, y - 1, t, r))
                {
                    return true;
                }
            }
            return false;
        }

        public bool BlockedD(int x, int y, int w, int h, bool t, bool r)
        {
            if (y >= MapH - h)
            {
                return true;
            }
            for (int i = 0; i < w; i++)
            {
                if (Blocked(x + i, y + h, t, r))
                {
                    return true;
                }
            }
            return false;
        }

        public bool BlockedUL(int x, int y, int w, int h, bool t, bool r)
        {
            if (x < 1 || y < 1)
            {
                return true;
            }
            for (int i = 0; i < w; i++)
            {
                if (Blocked(x - 1 + i, y - 1, t, r))
                {
                    return true;
                }
            }
            for (int i = 0; i < h; i++)
            {
                if (Blocked(x - 1, y - 1 + i, t, r))
                {
                    return true;
                }
            }
            return false;
        }

        public bool BlockedUR(int x, int y, int w, int h, bool t, bool r)
        {
            if (!(x < MapW - w && y > 0))
            {
                return true;
            }
            for (int i = 0; i < w; i++)
            {
                if (Blocked(x + w - i, y - 1, t, r))
                {
                    return true;
                }
            }
            for (int i = 0; i < h; i++)
            {
                if (Blocked(x + w, y - 1 + i, t, r))
                {
                    return true;
                }
            }
            return false;
        }

        public bool BlockedDL(int x, int y, int w, int h, bool t, bool r)
        {
            if (x < 1 || y >= MapH - h)
            {
                return true;
            }
            for (int i = 0; i < w; i++)
            {
                if (Blocked(x - 1 + i, y + h, t, r))
                {
                    return true;
                }
            }
            for (int i = 0; i < h; i++)
            {
                if (Blocked(x - 1, y + h - i, t, r))
                {
                    return true;
                }
            }
            return false;
        }

        public bool BlockedDR(int x, int y, int w, int h, bool t, bool r)
        {
            if (!(x < MapW - w && y < MapH - h))
            {
                return true;
            }
            for (int i = 0; i < w; i++)
            {
                if (Blocked(x + w - i, y + h, t, r))
                {
                    return true;
                }
            }
            for (int i = 0; i < h; i++)
            {
                if (Blocked(x + w, y + h - i, t, r))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Moves a box <paramref name="steps"/> times one cell in <paramref name="dir"/> with collision, sliding along
        /// walls and climbing one-cell steps.
        /// </summary>
        public void MoveSteps(ref int dir, int steps, ref int x, ref int y, int w, int h, bool isPlayer, bool t, bool r)
        {
            int d = dir & 0xf;
            bool physicsOn = Player.PhysicsOn != 0;
            if (physicsOn && d is not (1 or 2 or 4 or 5 or 6 or 8 or 9 or 10))
            {
                if (!isPlayer || (Player.StateFlags & PlayerState.FlagTransforming) == 0)
                {
                    d = 8;
                }
            }
            bool plane = isPlayer && Player.Form == PlayerState.Plane;
            bool horizOK = !isPlayer || Player.Form == PlayerState.Plane || Player.MoveEnable != 0;
            bool noPhys = isPlayer && !physicsOn;

            switch (d)
            {
                case 1:
                case 2:
                    if (!horizOK)
                    {
                        break;
                    }
                    for (int i = 0; i < steps; i++)
                    {
                        bool left = d == 1;
                        bool side = left ? BlockedL(x, y, w, h, t, r) : BlockedR(x, y, w, h, t, r);
                        if (!side || noPhys)
                        {
                            x += left ? -1 : 1;
                            if (isPlayer && physicsOn && Player.Form == PlayerState.Walker && !BlockedD(x, y, w, h, t, r))
                            {
                                y++; // Walk down a one-cell step
                            }
                        }
                        else if (isPlayer)
                        {
                            bool up = left ? BlockedUL(x, y, w, h, t, r) : BlockedUR(x, y, w, h, t, r);
                            if (Player.Form == PlayerState.Plane)
                            {
                                bool down = left ? BlockedDL(x, y, w, h, t, r) : BlockedDR(x, y, w, h, t, r);
                                if (!up)
                                {
                                    x += left ? -1 : 1;
                                    y--;
                                }
                                else if (!down)
                                {
                                    x += left ? -1 : 1;
                                    y++;
                                }
                                else
                                {
                                    PlaneStuck(ref dir, ref x, ref y, w, h, t, r);
                                    break;
                                }
                            }
                            else if (!up)
                            {
                                x += left ? -1 : 1;
                                y--;
                                Player.Falling = 0; // Climb a one-cell step
                            }
                        }
                    }
                    break;

                case 4:
                    for (int i = 0; i < steps; i++)
                    {
                        if (!BlockedU(x, y, w, h, t, r) || noPhys)
                        {
                            y--;
                            continue;
                        }
                        if (isPlayer && Player.Form == PlayerState.Walker && Player.Jumping != 0)
                        {
                            Player.JumpCount = jumpMax + 1; // Head hit ends the jump
                        }
                        if (plane)
                        {
                            if (!BlockedUL(x, y, w, h, t, r))
                            {
                                x--;
                                y--;
                            }
                            else if (!BlockedUR(x, y, w, h, t, r))
                            {
                                x++;
                                y--;
                            }
                            else
                            {
                                PlaneStuck(ref dir, ref x, ref y, w, h, t, r);
                                break;
                            }
                        }
                    }
                    break;

                case 8:
                    for (int i = 0; i < steps; i++)
                    {
                        if (!BlockedD(x, y, w, h, t, r) || noPhys)
                        {
                            y++;
                            continue;
                        }
                        if (plane)
                        {
                            if (!BlockedDL(x, y, w, h, t, r))
                            {
                                x--;
                                y++;
                            }
                            else if (!BlockedDR(x, y, w, h, t, r))
                            {
                                x++;
                                y++;
                            }
                            else
                            {
                                PlaneStuck(ref dir, ref x, ref y, w, h, t, r);
                                break;
                            }
                        }
                    }
                    break;

                case 5:
                case 6:
                    if (!horizOK)
                    {
                        break;
                    }
                    for (int i = 0; i < steps; i++)
                    {
                        bool left = d == 5;
                        bool diag = left ? BlockedUL(x, y, w, h, t, r) : BlockedUR(x, y, w, h, t, r);
                        if (!diag || noPhys)
                        {
                            y--;
                            x += left ? -1 : 1;
                        }
                        else if (!(left ? BlockedL(x, y, w, h, t, r) : BlockedR(x, y, w, h, t, r)))
                        {
                            x += left ? -1 : 1;
                            dir = left ? 1 : 2;
                        }
                        else if (!BlockedU(x, y, w, h, t, r))
                        {
                            y--;
                            dir = 4;
                        }
                        else if (plane)
                        {
                            PlaneStuck(ref dir, ref x, ref y, w, h, t, r);
                            break;
                        }
                    }
                    break;

                case 9:
                case 10:
                    if (!horizOK)
                    {
                        break;
                    }
                    for (int i = 0; i < steps; i++)
                    {
                        bool left = d == 9;
                        bool diag = left ? BlockedDL(x, y, w, h, t, r) : BlockedDR(x, y, w, h, t, r);
                        if (!diag || noPhys)
                        {
                            y++;
                            x += left ? -1 : 1;
                        }
                        else if (!(left ? BlockedL(x, y, w, h, t, r) : BlockedR(x, y, w, h, t, r)))
                        {
                            x += left ? -1 : 1;
                            dir = left ? 1 : 2;
                        }
                        else if (!BlockedD(x, y, w, h, t, r))
                        {
                            y++;
                            dir = 8;
                        }
                        else if (plane)
                        {
                            Player.Jumping = 0;
                            PlaneStuck(ref dir, ref x, ref y, w, h, t, r);
                            break;
                        }
                        else if (!(left ? BlockedUL(x, y, w, h, t, r) : BlockedUR(x, y, w, h, t, r)))
                        {
                            x += left ? -1 : 1;
                            y--; // Climb a step
                        }
                    }
                    break;
            }

            if (x > MapW - w)
            {
                x = MapW - w;
            }
            if (x < 0)
            {
                x = 0;
            }
            if (y > MapH - h)
            {
                y = MapH - h;
            }
            if (y < 0)
            {
                y = 0;
            }
        }

        public (int X, int Y) MoveBox(int x, int y, int dir, int steps, int w, int h, bool checkEnemy, bool raw)
        {
            int d = dir;
            MoveSteps(ref d, steps, ref x, ref y, w, h, isPlayer: false, checkEnemy, raw);
            return (x, y);
        }

        public bool AreaSolid(int x, int y, int w, int h)
        {
            for (int row = y; row < y + h; row++)
            {
                for (int col = x; col < x + w; col++)
                {
                    if (col < 0 || col >= MapW || row < 0 || row >= MapH || Map.IsSolid(row * MapW + col))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
