namespace Thexder.Core.Game
{
    // Player movement.
    internal sealed partial class ThexderGame
    {
        // Transform box per strip index form*3+target and reversed playback.
        private static readonly int[] TransformW = { 0, 5, 4, 5, 0, 5, 4, 5, 0 };
        private static readonly int[] TransformH = { 0, 6, 6, 6, 0, 5, 6, 5, 0 };
        private static readonly bool[] TransformReversed = { false, false, false, true, false, false, true, true, false };

        // RequestTransform relocation tries.
        private static readonly int[] TransformDx = { 0, 0, 0, -1, -2, -3, 1, 2, 3, 1, 2, -1, -2, -3 };
        private static readonly int[] TransformDy = { -1, -2, -3, -1, -2, -3, -1, -2, -3, 0, 0, 0, 0, 0 };

        // First world where each form is available; the cheats lower them.
        public int[] FormMinWorld { get; } = { 1, 2, 3 };

        private int jumpMax;
        private int deferredTankY;
        private int transportDestX;
        private int transportDestY;

        public int PlayerW => FormW[Player.Form];

        public int PlayerH => FormH[Player.Form];

        private bool PhysicsOn => Player.PhysicsOn != 0;

        /// <summary>The walker collides with a 2-cell-wide body inside its 4-cell sprite.</summary>
        private void BodyIn(ref int x, ref int w)
        {
            if ((Player.StateFlags & PlayerState.FlagTransforming) == 0 && Player.Form == PlayerState.Walker)
            {
                x += 1;
                w -= 2;
            }
        }

        /// <summary>Back from body to sprite coordinates.</summary>
        private void BodyOut(ref int x, int spriteW)
        {
            if ((Player.StateFlags & PlayerState.FlagTransforming) == 0 && Player.Form == PlayerState.Walker)
            {
                if (spriteW + x > MapW - 1)
                {
                    x -= 2;
                    return;
                }
                if (x > 0)
                {
                    x -= 1;
                }
            }
        }

        /// <summary>Standing on a slide-floor tile (0x3B..0x42)?</summary>
        private bool OnSlideFloor(int x, int w)
        {
            if (!PhysicsOn)
            {
                return false;
            }
            for (int i = 0; i < w; i++)
            {
                int idx = Map.Wrap16((FormH[Player.Form] + Player.Y) * MapW + i + x);
                if (idx <= Map.Count && idx < Map.Count && Map.Tile(idx) >= 0x3b && Map.Tile(idx) <= 0x42)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The robot's physics step.</summary>
        private void MovePlayer(int x0, int y0, int dir, int speed, int w, int h, bool isPlayer, bool checkEnemy, bool raw)
        {
            int dir0 = dir;
            int bw = w;
            int x = x0, y = y0;
            bool busy = Player.Busy;
            int steps = speed;
            var p = Player;
            if (LevelGfxReady && !busy)
            {
                BodyIn(ref x, ref bw);
                if (!PlanPlayerStep(ref dir, ref steps, x, y, bw, h, checkEnemy, raw))
                {
                    return;
                }
            }

            if ((p.StateFlags & 0x39) == 0)
            {
                MoveSteps(ref dir, steps, ref x, ref y, bw, h, isPlayer: true, checkEnemy, raw);
            }
            if (x == x0 && y == y0 && p.Form == PlayerState.Plane && !busy && (p.StateFlags & PlayerState.FlagTransforming) == 0 && dir == dir0)
            {
                MoveSteps(ref dir, steps, ref x, ref y, bw, h, isPlayer: true, checkEnemy, raw); // Plane: second try
            }
            if (PhysicsOn && p.Form == PlayerState.Walker && p.Jumping != 0 && BlockedD(x, y, bw, h, checkEnemy, raw))
            {
                p.Speed = p.SavedSpeed;
                if (p.JumpCount != 0)
                {
                    p.Landed = 1;
                }
                p.Jumping = 0;
            }
            p.LastDir = dir;
            p.PrevFacing = p.Facing;
            if (BlockedD(x, y, bw, h, checkEnemy, raw))
            {
                p.Falling = 0;
            }
            if (LevelGfxReady && !busy)
            {
                BodyOut(ref x, w);
            }
            p.X = x;
            p.Y = y;
        }

        /// <summary>
        /// The form's rules before the step (flight, gravity, jumps, landing, jet pack) for the body box: adjusts
        /// <paramref name="dir"/> and <paramref name="steps"/> (the speed on entry); false when the player stays put.
        /// </summary>
        private bool PlanPlayerStep(ref int dir, ref int steps, int x, int y, int w, int h, bool checkEnemy, bool raw)
        {
            var p = Player;
            if (p.Form != PlayerState.Plane && BlockedD(x, y, w, h, checkEnemy, raw) && OnSlideFloor(x, w))
            {
                return false; // FloorEffects slides the player instead
            }
            if (p.Form == PlayerState.Plane)
            {
                if (Inv.IsActive(ItemType.HoverPack))
                {
                    if (dir == 0)
                    {
                        return false;
                    }
                }
                else if (dir == 0)
                {
                    dir = p.PlaneDir != 0 ? p.PlaneDir : p.LastDir; // Keep flying
                }
                else
                {
                    p.PlaneDir = dir;
                }
            }
            if (p.Form == PlayerState.Tank && PhysicsOn)
            {
                dir = (dir & ~4) | 8; // The tank never goes up and always has gravity
            }

            p.Falling = 0;
            if (p.Form != PlayerState.Plane && p.Jumping == 0 && PhysicsOn && !BlockedD(x, y, w, h, checkEnemy, raw))
            {
                p.Falling = 1;
                dir = (dir & ~4) | 8;
            }

            if (PhysicsOn && p.Form == PlayerState.Walker)
            {
                if (p.Jumping != 0)
                {
                    if (dir != 0 && ((dir & 4) == 0 || !BlockedU(x, y, w, h, checkEnemy, raw)))
                    {
                        p.JumpCount++;
                    }
                    else
                    {
                        p.JumpCount = jumpMax + 1; // Keys released or head hit: end the rise
                    }
                    if (p.JumpCount > jumpMax)
                    {
                        dir = (dir & ~4) | 8;
                    }
                }

                p.Landed = 0;
                if (BlockedD(x, y, w, h, checkEnemy, raw))
                {
                    if (p.Jumping != 0)
                    {
                        p.Landed = 1;
                        p.Speed = p.SavedSpeed;
                        steps = p.SavedSpeed;
                    }
                    p.Jumping = 0;
                }
                if ((dir & 4) != 0 && p.Jumping == 0 && BlockedD(x, y, w, h, checkEnemy, raw))
                {
                    if ((p.PrevFacing == 2 && (dir & 1) != 0) || (p.PrevFacing == 1 && (dir & 2) != 0))
                    {
                        dir &= ~4; // No jump on a frame that reverses direction
                    }
                    if ((dir & 4) != 0)
                    {
                        steps = 1;
                        p.JumpCount = 0;
                        p.Jumping = 1;
                        p.SavedSpeed = p.Speed;
                        jumpMax = FormH[p.Form] + 3;
                        p.Speed = 1;
                        if (Inv.IsActive(ItemType.JumpBoots))
                        {
                            jumpMax += Items[ItemType.JumpBoots].JumpBonus;
                        }
                    }
                }
            }

            if (PhysicsOn && p.Jumping == 0 && Inv.IsActive(ItemType.JetPack))
            {
                steps += Items[ItemType.JetPack].JetpackBonus;
                if (steps >= 10)
                {
                    steps = 10;
                }
            }
            return true;
        }

        /// <summary>The plane is blocked ahead and on both diagonals: try to become the robot, else bounce.</summary>
        private void PlaneStuck(ref int dir, ref int x, ref int y, int w, int h, bool t, bool r)
        {
            Player.X = x;
            Player.Y = y;
            RequestTransform(PlayerState.Walker);
            if ((Player.StateFlags & PlayerState.FlagTransforming) == 0)
            {
                PlaneBounce(ref dir, x, y, w, h, t, r);
            }
            else
            {
                x = Player.X;
                y = Player.Y;
            }
        }

        /// <summary>Reverse the flight direction if the reverse way is free.</summary>
        private void PlaneBounce(ref int dir, int x, int y, int w, int h, bool t, bool r)
        {
            int last = Player.LastDir;
            int nd = dir switch
            {
                1 => BlockedR(x, y, w, h, t, r) ? last : 2,
                2 => BlockedL(x, y, w, h, t, r) ? last : 1,
                4 => BlockedD(x, y, w, h, t, r) ? last : 8,
                5 => BlockedDR(x, y, w, h, t, r) ? last : 10,
                6 => BlockedDL(x, y, w, h, t, r) ? last : 9,
                8 => BlockedU(x, y, w, h, t, r) ? last : 4,
                9 => BlockedUR(x, y, w, h, t, r) ? last : 6,
                10 => BlockedUL(x, y, w, h, t, r) ? last : 5,
                _ => 0,
            };
            dir = nd;
            Player.PlaneDir = nd;
        }

        /// <summary>Moves the player n cells in dir with collision; true if it moved.</summary>
        private bool PushPlayer(int dir, int n)
        {
            var p = Player;
            int x = p.X, y = p.Y;
            int w = FormW[p.Form], h = FormH[p.Form];
            if ((p.StateFlags & PlayerState.FlagTransforming) != 0)
            {
                int i = p.Form * 3 + p.TransformTarget;
                w = TransformW[i];
                h = TransformH[i];
            }
            BodyIn(ref x, ref w);
            MoveSteps(ref dir, n, ref x, ref y, w, h, isPlayer: true, t: true, r: false);
            BodyOut(ref x, FormW[p.Form]);
            if (p.X == x && p.Y == y)
            {
                return false;
            }
            p.X = x;
            p.Y = y;
            return true;
        }

        /// <summary>Conveyors, slide floors and transporter pads.</summary>
        private void FloorEffects()
        {
            var p = Player;
            p.MoveEnable = 1;
            int bx = p.X, bw = FormW[p.Form];
            int h = FormH[p.Form];
            BodyIn(ref bx, ref bw);
            if (!BlockedD(bx, p.Y, bw, h, true, false))
            {
                return; // Only when standing on something
            }
            if (p.Form != PlayerState.Plane)
            {
                bool toRight = false, toLeft = false;
                for (int i = 0; i < bw; i++)
                {
                    int idx = Map.Wrap16((h + p.Y) * MapW + i + bx);
                    if (idx < Map.Count + 1 && idx < Map.Count)
                    {
                        int tile = Map.Tile(idx);
                        if (tile >= 0x43 && tile <= 0x45)
                        {
                            toRight = true;
                            break;
                        }
                        if (tile >= 0x46 && tile <= 0x48)
                        {
                            toLeft = true;
                            break;
                        }
                    }
                }
                if (toRight || toLeft)
                {
                    PushPlayer(toLeft ? 1 : 2, 1);
                }
                else if (OnSlideFloor(bx, bw))
                {
                    if (p.LastDir != 1 && p.LastDir != 2)
                    {
                        p.LastDir = p.Facing;
                    }
                    if (!PushPlayer(p.LastDir, 2))
                    {
                        p.LastDir = p.LastDir == 2 ? 1 : 2; // Bounced: reverse
                        p.Facing = p.LastDir;
                    }
                }
            }

            if (PhysicsOn && (p.StateFlags & 0x30) == 0)
            {
                int w = FormW[p.Form];
                for (int row = 0; row < h; row++)
                {
                    for (int col = 0; col < w; col++)
                    {
                        int idx = Map.Wrap16((row + p.Y) * MapW + col + p.X);
                        if (idx >= Map.Count || (Map.B3(idx) & 0x3f) != 0x13)
                        {
                            continue;
                        }
                        int o = FindLevelObject(p.X + col, row + p.Y);
                        if (o < 0)
                        {
                            continue;
                        }
                        int dx = Objects[o].DestX1 - 1, dy = Objects[o].DestY1 - 1;
                        int px = dx, pw = w;
                        BodyIn(ref px, ref pw);
                        bool ok = true;
                        for (int k = 0; k < h; k++)
                        {
                            if (BlockedD(px, dy + k, pw, 0, true, false))
                            {
                                ok = false;
                                break;
                            }
                        }
                        if (!ok)
                        {
                            continue;
                        }
                        if (TransportSprite(p.Form) != null)
                        {
                            if (p.Facing == 1)
                            {
                                p.Facing = 2;
                                p.Frame = 0;
                            }
                            else
                            {
                                p.Facing = 1;
                                p.Frame = 5;
                            }
                            p.StateFlags |= PlayerState.FlagTurning | PlayerState.FlagTransportOut;
                            transportDestX = dx;
                            transportDestY = dy;
                        }
                        else
                        {
                            p.X = dx;
                            p.Y = dy;
                        }
                        Sounds.Play(0x8d);
                    }
                }
            }
        }

        /// <summary>First used object at a cell, or -1.</summary>
        private int FindLevelObject(int x, int y)
        {
            for (int i = 0; i < Objects.Length; i++)
            {
                if (Objects[i].Kind != 0 && (short)Objects[i].X == (short)x && (short)Objects[i].Y == (short)y)
                {
                    return i;
                }
            }
            return -1;
        }

        private int FreeLevelObject()
        {
            for (int i = 0; i < Objects.Length; i++)
            {
                if (Objects[i].Kind == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>A 5 x 6 area for the transform animation.</summary>
        private int AreaBlocked5x6(int x, int y)
        {
            int r = 0;
            for (int row = y; row < y + 6; row++)
            {
                for (int col = x; col < x + 5; col++)
                {
                    if (col >= 0 && col < MapW && row >= 0 && row * MapW < Map.Count)
                    {
                        int idx = row * MapW + col;
                        if (Map.IsSolid(idx))
                        {
                            r |= 1;
                        }
                        int occ = Map.B2(idx) & 0x7f;
                        if (occ != 0x7f)
                        {
                            r |= occ;
                        }
                    }
                    else
                    {
                        r = 1;
                    }
                }
            }
            return r;
        }

        public void RequestTransform(int to)
        {
            var p = Player;
            if ((p.StateFlags & PlayerState.FlagTransforming) == 0 && to != p.Form && FormMinWorld[to] <= World)
            {
                bool blocked = AreaBlocked5x6(p.X, p.Y) != 0;
                if (blocked)
                {
                    for (int k = 0; k < TransformDx.Length; k++)
                    {
                        int nx = p.X + TransformDx[k], ny = p.Y + TransformDy[k];
                        if (AreaBlocked5x6(nx, ny) == 0)
                        {
                            p.X = nx;
                            p.Y = ny;
                            blocked = false;
                            break;
                        }
                    }
                }
                if (!blocked)
                {
                    p.StateFlags |= PlayerState.FlagTransforming;
                    p.Jumping = 0;
                    p.Frame = 0;
                    p.TransformTarget = to;
                    Sounds.PlayChannel(0x41, 0);
                    SetFormWeapons(to);
                }
            }
            else if (to != p.Form && World < FormMinWorld[to])
            {
                if (to == PlayerState.Plane)
                {
                    Sounds.Play(0x92);
                }
                else if (to == PlayerState.Tank)
                {
                    Sounds.Play(0x93);
                }
            }
        }

        /// <summary>The default weapon of a form.</summary>
        private void SetFormWeapons(int form)
        {
            var p = Player;
            p.Weapon = FormDefaultWeapon[form];
            if (form == PlayerState.Plane)
            {
                if (p.Weapon == 1)
                {
                    p.AltWeapon = Inv.Count[8] != 0 ? 8 : 6;
                }
                else if (p.Weapon == 8)
                {
                    p.AltWeapon = Inv.Count[1] != 0 ? 1 : 6;
                }
                else
                {
                    p.AltWeapon = Inv.Count[1] != 0 ? 1 : 8;
                }
            }
            WeaponCursor = p.Weapon - 1;
            AltWeaponCursor = p.AltWeapon - 1;
        }

        /// <summary>The end of a transform (PlayerDraw, frame > 11).</summary>
        private void TransformComplete()
        {
            var p = Player;
            p.Frame = 0;
            p.StateFlags &= ~PlayerState.FlagTransforming;
            EraseMarks(p.X, p.Y, FormW[p.Form], FormH[p.Form]);
            p.Form = p.TransformTarget;
            p.Speed = FormSpeed[p.Form];
            if (p.Form == PlayerState.Plane)
            {
                p.CmdDir = (p.CmdDir & ~3) | (p.Facing == 2 ? 2 : 1);
                p.PlaneDir = p.CmdDir;
                p.Frame = PlaneFrame[p.CmdDir & 0xf];
            }
            else if (p.Form == PlayerState.Tank)
            {
                deferredTankY = p.Y + 3; // The tank sits at the bottom of the robot's box
            }
            StampMarks(p.X, p.Y, FormW[p.Form], FormH[p.Form]);
            if ((p.StateFlags & PlayerState.FlagGoingHome) != 0)
            {
                StartTurn();
            }
        }

        /// <summary>Turn around (used by the ending).</summary>
        public void StartTurn()
        {
            var p = Player;
            if (TurnSprite(p.Form) != null)
            {
                p.StateFlags |= PlayerState.FlagTurning;
                if (p.Facing == 1)
                {
                    p.Facing = 2;
                    p.Frame = 0;
                }
                else
                {
                    p.Facing = 1;
                    p.Frame = 5;
                }
            }
        }

        // ---- Occupancy marks and the map dot -------------------------------------------------------------

        /// <summary>Clears the player's occupancy marks (b2 0x7F) and map dots in a box.</summary>
        private void EraseMarks(int x, int y, int w, int h)
        {
            for (int row = y; row < y + h; row++)
            {
                for (int col = x; col < x + w; col++)
                {
                    if (col < MapW && row >= 0 && row * MapW < Map.Count && col >= 0)
                    {
                        int idx = row * MapW + col;
                        if ((Map.B2(idx) & 0x7f) == 0x7f)
                        {
                            Map.SetB2(idx, (byte)(Map.B2(idx) & 0x80));
                        }
                        MapSurface.Pixels[idx] = 0;
                    }
                }
            }
        }

        /// <summary>Marks the player's box in the map (b2 0x7F) and draws it on the planet map (colour 0x10).</summary>
        private void StampMarks(int x, int y, int w, int h)
        {
            for (int row = y; row < y + h; row++)
            {
                for (int col = x; col < x + w; col++)
                {
                    if (col < MapW && row >= 0 && row * MapW < Map.Count && col >= 0)
                    {
                        int idx = row * MapW + col;
                        if ((Map.B2(idx) & 0x7f) == 0)
                        {
                            Map.SetB2(idx, (byte)(Map.B2(idx) | 0x7f));
                        }
                        MapSurface.Pixels[idx] = 0x10;
                    }
                }
            }
        }
    }
}
