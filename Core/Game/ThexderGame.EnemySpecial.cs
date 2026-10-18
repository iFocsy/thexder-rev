namespace Thexder.Core.Game
{
    // Per-type enemy code: firing, contact specials, the Necromorgue's teleport and the rock rain of Thor and the
    // Vipercoil.
    internal sealed partial class ThexderGame
    {
        private const int EnemyAimAtPlayer = 0x7f;

        private static readonly int[] EnemyMegaGunDX = { 2, 3, 5, 6, 6, 6, 5, 4, 2, 0, -1, -2, -2, -2, -1, 0 };
        private static readonly int[] EnemyMegaGunDY = { 6, 6, 5, 4, 2, 0, -1, -2, -2, -2, -1, 0, 2, 4, 5, 6 };
        private static readonly int[] EnemyRockColumns = { -7, -3, 0, 3, 7 };

        private bool EnemyFacesPlayer(EnemyRecord r) =>
            (r.Flags & EnemyRecord.FlagFacingLeft) != 0 ? Player.X < r.X : r.X < Player.X;

        // "atk(f)": this attack frame was reached this tick
        private static bool EnemyOnAttackFrame(EnemyRecord r, int frame) => r.Attacking != 0 && r.AnimTick == 0 && r.Frame == frame;

        /// <summary>Shots, bombs and spawned enemies on the right attack frames.</summary>
        private void EnemyFire(int i, EnemyRecord r, EnemyType type)
        {
            bool fire = false;
            int aim = EnemyAimAtPlayer;
            int sx = 0, sy = 0;
            int shot = type.Weapons[r.Weapon % 6].Shot;
            switch (r.Type)
            {
                case 0x01:
                    // Auto Turret: only at a plane
                    if (Player.Form == PlayerState.Plane && FrameCounter % 6 == 0)
                    {
                        fire = true;
                        Sounds.Play(0x87);
                    }
                    break;
                case 0x03:
                    if (EnemyOnAttackFrame(r, 0))
                    {
                        EnemyPickWeapon(ref shot, type.Weapons);
                        if (EnemyFacesPlayer(r))
                        {
                            fire = true;
                            Sounds.Play(0x2d);
                        }
                    }
                    break;
                case 0x08:
                    // Hop Crusher, second attack
                    if (r.Attacking != 0 && r.AnimTick == 0 && r.AtkIdx == 1 && (r.Frame == 0 || r.Frame == 5) && EnemyFacesPlayer(r))
                    {
                        fire = true;
                        Sounds.Play(0x62);
                    }
                    break;
                case 0x0a:
                    if (EnemyOnAttackFrame(r, 0) && EnemyFacesPlayer(r))
                    {
                        fire = true;
                        Sounds.Play(0x71);
                    }
                    break;
                case 0x14:
                    // Pulsar Czar: one shot per aim frame, toward the screen edge in the aimed direction
                    if (r.Attacking != 0 && r.AnimTick == 0 && (r.Frame & 1) == 0)
                    {
                        fire = true;
                        aim = 0;
                        Sounds.Play(0x69);
                    }
                    break;
                case 0x15:
                    // Tech Wraith
                    if (EnemyOnAttackFrame(r, 0) && EnemyFacesPlayer(r))
                    {
                        fire = true;
                        Sounds.Play(0x10);
                    }
                    break;
                case 0x17:
                    // Demon: boomerang, or a grenade dropped on the player while holding the second attack
                    if (r.Attacking != 0 && r.AnimTick == 0)
                    {
                        if (r.AtkIdx == 0 && r.Frame == 5)
                        {
                            if (EnemyFacesPlayer(r))
                            {
                                fire = true;
                            }
                        }
                        else if (r.AtkIdx == 1 && r.Frame == 11 && r.Aux == 1)
                        {
                            int bx = Player.Facing != 1 ? FormW[Player.Form] + Player.X - 1 : Player.X;
                            SpawnBomb((ushort)bx, (byte)(FormH[Player.Form] + Player.Y - 2), 0x21);
                        }
                    }
                    break;
                case 0x18:
                    if (r.Attacking != 0 && r.AnimTick == 0 && r.AtkIdx == 1 && r.Frame == 5 && EnemyFacesPlayer(r))
                    {
                        fire = true;
                    }
                    break;
                case 0x1b:
                    // Bug Breath: throws a Bug Bomb on frame 6 (no attack test)
                    if (r.AnimTick == 0 && r.Frame == 6)
                    {
                        var bomb = EnemyTypes[0x0c];
                        if (bomb != null)
                        {
                            int bx = (ushort)(r.X - bomb.W), by = (ushort)(r.Y + 5);
                            if (!BlockedL(bx + 1, by, bomb.W, bomb.H, true, false))
                            {
                                EnemySpawn(0x0c, bx, by, 0x13, bomb.Hp, ViewWidthCells, 2);
                                Sounds.Play(0x88);
                            }
                        }
                        r.AnimTick = type.AnimDelay;
                    }
                    break;
                case 0x1c:
                    if (EnemyOnAttackFrame(r, 5) && EnemyFacesPlayer(r))
                    {
                        fire = true;
                    }
                    break;
                case 0x1d:
                    // Vipercoil, second attack
                    if (EnemyOnAttackFrame(r, 9) && r.AtkIdx == 1)
                    {
                        EnemyRockRain(i, type);
                    }
                    break;
                case 0x1e:
                    // Primus Crawler: bombs when above and near the player
                    if (r.AnimTick == 0 && r.Frame == 0 && r.Y < Player.Y && Math.Abs(Player.X - r.X) < 20
                        && Math.Abs(Player.Y - r.Y) < 20 && Random.Next() % 10 < 5)
                    {
                        int bx = (r.Flags & EnemyRecord.FlagFacingLeft) == 0 ? r.W + r.X - 1 : r.X;
                        SpawnBomb((ushort)bx, (byte)(r.H + r.Y - 1), 0x22);
                    }
                    break;
                case 0x23:
                    // Mega Gun: launches a Mega Mine every 15 ticks, one aim sector off at random
                    if (FrameCounter % 15 == 0)
                    {
                        int roll = Random.Next();
                        int k = r.Frame / 3;
                        int mx = (short)(EnemyMegaGunDX[k & 15] + r.X);
                        int my = (short)(EnemyMegaGunDY[k & 15] + r.Y);
                        if (mx < 0)
                        {
                            mx = 0;
                        }
                        if (my < 0)
                        {
                            my = 0;
                        }
                        var mine = EnemyTypes[0x19];
                        if (mine != null && !BlockedL(mx + 1, my, mine.W, mine.H, true, false))
                        {
                            int j = EnemySpawn(0x19, mx, my, 0x14, mine.Hp, ViewWidthCells, 2);
                            if (roll < 0x2a27)
                            {
                                k = k + 1 < type.Frames ? k + 1 : 0; // type.Frames is 48: sector 16 keeps dir 1
                            }
                            else if (roll < 0x544e)
                            {
                                k = (k == 0 ? type.Frames : k) - 1;
                            }
                            int d = (k & 0xffff) switch
                            {
                                0 or 1 => 8,
                                2 or 3 => 10,
                                4 or 5 => 2,
                                6 or 7 => 6,
                                8 or 9 => 4,
                                10 or 11 => 5,
                                12 or 13 => 1,
                                14 or 15 => 9,
                                _ => -1,
                            };
                            if (d >= 0)
                            {
                                Enemies[j].Dir = d;
                            }
                            Sounds.Play(0x46);
                            r.Frame = (r.Frame + 1) & 0xffff; // Recoil frames (mode 0)
                        }
                    }
                    break;
                case 0x26:
                    // Dragon Lord
                    if (EnemyOnAttackFrame(r, 7) && EnemyFacesPlayer(r))
                    {
                        fire = true;
                    }
                    break;
                case 0x27:
                    // Tri-turret: only at a plane, toward the screen edge
                    if (Player.Form == PlayerState.Plane && FrameCounter % 6 == 0)
                    {
                        fire = true;
                        aim = 0;
                        Sounds.Play(0x68);
                    }
                    break;
                case 0x28:
                    if (EnemyOnAttackFrame(r, 1) && EnemyFacesPlayer(r))
                    {
                        fire = true;
                    }
                    break;
                case 0x29:
                    // Primus Flyer: level with and facing the player, every fourth tick
                    if (r.AnimTick == 0 && (r.Frame == 0 || r.Frame == 8) && Math.Abs(Player.Y - r.Y) < 25
                        && Math.Abs(Player.X - r.X) < 30 && EnemyFacesPlayer(r) && FrameCounter % 4 == 0)
                    {
                        fire = true;
                        Sounds.Play(0x10);
                    }
                    break;
                case 0x2a:
                    // Primus Walker: first frame of each aim pose
                    if (r.Attacking != 0 && r.AnimTick == 0 && r.Frame % 3 == 0 && EnemyFacesPlayer(r))
                    {
                        aim = 0;
                        fire = true;
                    }
                    break;
                case 0x31:
                    // Thor, second attack
                    if (EnemyOnAttackFrame(r, 9) && r.AtkIdx == 1)
                    {
                        EnemyRockRain(i, type);
                        Sounds.Play(0x16);
                    }
                    break;
                case 0x34:
                    // Jackaloid: dagger toward the screen edge it faces (no facing test)
                    if (EnemyOnAttackFrame(r, 5) && r.AtkIdx == 0)
                    {
                        aim = 0;
                        fire = true;
                        (sx, sy) = EnemyEdgeTarget(r);
                    }
                    break;
                case 0x35:
                    // Bug Snake
                    if (EnemyOnAttackFrame(r, 5) && EnemyFacesPlayer(r))
                    {
                        fire = true;
                        Sounds.Play(0x6a);
                    }
                    break;
                case 0x37:
                    // Death Plumber: acid stream toward the screen edge, on the second tick of the held frame 5
                    if (EnemyOnAttackFrame(r, 5) && r.Aux == 1 && EnemyFacesPlayer(r))
                    {
                        aim = 0;
                        fire = true;
                        (sx, sy) = EnemyEdgeTarget(r);
                    }
                    break;
            }
            if (fire)
            {
                FireEvent(shot & 0xff, i, aim, sx, sy);
            }
        }

        // The point at the edge of the view the enemy faces, level with its middle (world pixels)
        private (int X, int Y) EnemyEdgeTarget(EnemyRecord r)
        {
            int col = (r.Flags & EnemyRecord.FlagFacingLeft) == 0 ? ViewWidthCells + CamX : CamX;
            return ((short)(col * S), (short)(((r.H >> 1) + r.Y) * S));
        }

        /// <summary>
        /// Kept literally: it compares and stores a weapon index in the shot id variable, which the shipped data never
        /// reaches.
        /// </summary>
        private void EnemyPickWeapon(ref int shot, EnemyWeapon[] weapons)
        {
            int count = 0;
            for (int k = 1; k < 6; k++)
            {
                if (weapons[k].Shot >= 0)
                {
                    count++;
                }
            }
            if (count > 1)
            {
                int k = shot == 1 ? 2 : 1;
                if (weapons[k].Chance != 0 && weapons[k].Chance * 0x147 <= Random.Next())
                {
                    shot = k;
                }
            }
        }

        /// <summary>
        /// EnemyRockRain: 3 to 5 rubble shots from the ceiling above five columns around the player, and a screen shake.
        /// </summary>
        private void EnemyRockRain(int i, EnemyType type)
        {
            if (ShakeRequest == 0)
            {
                ShakeRequest = 0x69;
            }
            Sounds.Play(100);
            int top = CamY;
            bool[] used = new bool[5];
            int n = Random.Next() % 3 + 3;
            while (n-- > 0)
            {
                int shot = type.Weapons[Random.Next() % 6].Shot;
                int pick;
                do
                {
                    pick = Random.Next() % 5;
                }
                while (used[pick]);
                used[pick] = true;
                int col = (short)((FormW[Player.Form] >> 2) + EnemyRockColumns[pick] + Player.X);
                int row = (Player.Y - 1) & 0xffff;
                int c = Map.Wrap16(row * MapW + col);
                while (row != 0 && top < row && (c >= Map.Count || Map.Tile(c) == 0 || (Map.B3(c) & 0xc0) != 0))
                {
                    row--;
                    c = Map.Wrap16(c - MapW);
                }
                FireEvent(shot & 0xff, i, 0, S * col, (row + 1) * S);
            }
        }

        /// <summary>Mines exploding on contact, electric fence sparks, pulsar pulses.</summary>
        private void EnemySpecialTouch(EnemyRecord r, int i)
        {
            var type = TypeOf(r);
            int px = Player.X, py = Player.Y, pw = FormW[Player.Form], ph = FormH[Player.Form];
            switch (r.Type)
            {
                case 0x04:
                case 0x0c:
                case 0x19:
                    // Mine, Bug Bomb, Mega Mine: explode when touching the ring around the player
                    if (r.State == EnemyRecord.Alive && type != null
                        && EnemyRectsIntersect(px - 1, py - 1, px + pw + 1, py + ph + 1, r.X, r.Y, r.X + r.W, r.Y + r.H))
                    {
                        DamagePlayer(type.TouchDmg, false);
                        if (ShakeRequest == 0)
                        {
                            ShakeRequest = 0x69;
                        }
                        EnemyDamage(i, (short)r.Hp);
                    }
                    break;
                case 0x06:
                {
                    // Electric Fence: a 6-cell spark toward the player
                    int left, right;
                    if (px < r.X)
                    {
                        right = r.X;
                        left = right - 6;
                    }
                    else
                    {
                        left = r.X + r.W;
                        right = left + 6;
                    }
                    if (type != null && EnemyRectsIntersect(px, py, px + pw, py + ph, left, r.Y, right, r.Y + r.H))
                    {
                        DamagePlayer(type.TouchDmg, false);
                        Sounds.PlayAt(2, 0, -800);
                        EnemyDraw(type.SprFlinchR, left, r.Y, 6, 2, Random.Next() % 4, Art.Tints.Identity);
                    }
                    break;
                }
                case 0x14:
                case 0x1f:
                    // Pulsar Czar, Pulsar: radial pulse every fourth animation cycle
                    if (r.State == EnemyRecord.Alive && r.Frame == 0)
                    {
                        if (r.Aux == 3)
                        {
                            int dx = Math.Abs((ushort)((r.W >> 1) + r.X) - EnemyPlayerCenterX);
                            int dy = Math.Abs((ushort)((r.H >> 1) + r.Y) - EnemyPlayerCenterY);
                            int d = 0;
                            if (dx < 5 || dy < 5)
                            {
                                d = 6;
                            }
                            else if (dx < 12 || dy < 12)
                            {
                                d = 4;
                            }
                            else if (dx < 19 || dy < 19)
                            {
                                d = 2;
                            }
                            if (Inv.IsActive(ItemType.LavaArmor) || Inv.IsActive(ItemType.AcidArmor))
                            {
                                d >>= 1;
                            }
                            else if (Inv.IsActive(ItemType.MegaArmor))
                            {
                                d = 0;
                            }
                            if (d != 0)
                            {
                                DamagePlayer(d, false);
                            }
                            r.Aux = 0;
                            return;
                        }
                        r.Aux = (r.Aux + 1) & 0xffff;
                    }
                    break;
            }
        }

        // Win32 IntersectRect: the rectangles share a non-empty area
        private static bool EnemyRectsIntersect(int l1, int t1, int r1, int b1, int l2, int t2, int r2, int b2) =>
            Math.Max(l1, l2) < Math.Min(r1, r2) && Math.Max(t1, t2) < Math.Min(b1, b2);

        /// <summary>
        /// The sunk Necromorgue reappears level with, above or below the player (first free box) and slides away from him
        /// until it touches a wall.
        /// </summary>
        private void EnemyTeleportNextToPlayer(EnemyRecord r)
        {
            int w = r.W, h = r.H;
            int px = Player.X, py = Player.Y, pw = FormW[Player.Form];
            bool fromRight = EnemyPlayerCenterX <= (ushort)((w >> 1) + r.X);
            bool Free(int x, int y) => !EnemyBoxBlocked(x, y, w, h, true, false);

            // Level with the player, on the other side, then away from him
            int x = (ushort)(fromRight ? px - w : px + pw);
            int y = py;
            if (Free(x, y))
            {
                if (fromRight)
                {
                    while (!BlockedL(x, y, w, h, true, false))
                    {
                        x = (x - 1) & 0xffff;
                    }
                }
                else
                {
                    while (!BlockedR(x, y, w, h, true, false))
                    {
                        x = (x + 1) & 0xffff;
                    }
                }
                Commit(x, y);
                return;
            }

            // Above, diagonally away (the near side is mirrored here)
            x = (ushort)(fromRight ? pw + px : px - w);
            y = py - h < 1 ? 0 : py - h;
            if (Free(x, y))
            {
                if (fromRight)
                {
                    int prev;
                    do
                    {
                        prev = x;
                        x = (x + 1) & 0xffff;
                        if (y != 0)
                        {
                            y--;
                        }
                    }
                    while (!BlockedUR(x - 1, y + 1, w, h, true, false));
                    if (x != 0)
                    {
                        x = prev;
                    }
                }
                else
                {
                    do
                    {
                        if (x != 0)
                        {
                            x--;
                        }
                        if (y != 0)
                        {
                            y--;
                        }
                    }
                    while (!BlockedUL(x + 1, y + 1, w, h, true, false));
                    x = (x + 1) & 0xffff;
                }
                Commit(x, (y + 1) & 0xffff);
                return;
            }

            // Below, diagonally away
            x = (ushort)(fromRight ? pw + px : px - w);
            y = (ushort)(py + h);
            if (Free(x, y))
            {
                int prevY;
                if (fromRight)
                {
                    int prevX;
                    do
                    {
                        prevX = x;
                        x = (x + 1) & 0xffff;
                        prevY = y;
                        y = (y + 1) & 0xffff;
                    }
                    while (!BlockedDR(x - 1, y - 1, w, h, true, false));
                    if (x != 0)
                    {
                        x = prevX;
                    }
                    Commit(x, y != 0 ? prevY : 0);
                }
                else
                {
                    do
                    {
                        prevY = y;
                        if (x != 0)
                        {
                            x--;
                        }
                        y = (prevY + 1) & 0xffff;
                    }
                    while (!BlockedDL(x + 1, y - 1, w, h, true, false));
                    Commit((x + 1) & 0xffff, prevY);
                }
            }

            void Commit(int cx, int cy)
            {
                r.X = cx & 0xffff;
                r.Y = cy & 0xffff;
            }
        }
    }
}
