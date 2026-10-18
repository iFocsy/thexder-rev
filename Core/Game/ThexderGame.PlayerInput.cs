namespace Thexder.Core.Game
{
    // Player input and firing.
    internal sealed partial class ThexderGame
    {
        private bool fireHeld;
        private bool firePressed;
        private int lastTarget = 99;

        /// <summary>Off by default here; never used while a demo plays.</summary>
        public bool SmartAim { get; set; }

        private bool SmartAiming => SmartAim && demoScript == null && !KioskMode;

        /// <summary>
        /// Smart aim: whether a beam from the muzzle (view pixels) reaches the box (cells) before any other solid cell,
        /// checked every half cell along the line.
        /// </summary>
        private bool LineOfFire(int mx, int my, int camX, int camY, int bx, int by, int bw, int bh)
        {
            double x0 = camX * S + mx, y0 = camY * S + my;
            double x1 = (bx + bw / 2.0) * S, y1 = (by + bh / 2.0) * S;
            int steps = (int)(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) / (S / 2.0)) + 1;
            for (int i = 1; i <= steps; i++)
            {
                int cx = (int)Math.Floor((x0 + (x1 - x0) * i / steps) / S);
                int cy = (int)Math.Floor((y0 + (y1 - y0) * i / steps) / S);
                if (cx >= bx && cx < bx + bw && cy >= by && cy < by + bh)
                {
                    return true;
                }
                if (cx < 0 || cy < 0 || cx >= MapW || cy >= MapH || Map.IsSolid(Map.Index(cx, cy)))
                {
                    return false;
                }
            }
            return true;
        }
        private int scanX, scanY;
        private int scanFacing;
        private long idleSince;

        /// <summary>Called when the codeword dialog combination is held (T + X).</summary>
        public event Action? CodewordRequested;

        private bool Key(GameAction action) => Input.Held(action);

        /// <summary>Touch: resets the idle-taunt timer.</summary>
        public void Touch() => idleSince = Clock();

        private bool Usable(int item, int form) => item >= 0 && item < Items.Length && Items[item].Usable[form];

        private bool Aimable(int weapon) => weapon >= 0 && weapon < ShotTypes.Length && ShotTypes[weapon].Aimed;

        /// <summary>Input: this frame's direction command, transforms and selection keys.</summary>
        private void ReadInput()
        {
            var p = Player;
            prevBoxX = p.X;
            prevBoxY = p.Y;
            prevBoxW = FormW[p.Form];
            prevBoxH = FormH[p.Form];
            skipRestOfFrame = false;
            if (NetActive)
            {
                NetClearRemoteMarks();
            }

            for (int row = p.Y; row < p.Y + FormH[p.Form]; row++)
            {
                for (int col = p.X; col < p.X + FormW[p.Form]; col++)
                {
                    int idx = row * MapW + col;
                    if (idx < Map.Count && (Map.B2(idx) & 0x7f) == 0x7f)
                    {
                        Map.SetB2(idx, (byte)(Map.B2(idx) & 0x80));
                    }
                }
            }
            if (deferredTankY != 0)
            {
                p.Y = deferredTankY;
                deferredTankY = 0;
            }
            p.CmdDir = 0;
            ReadMoveKeys(p);

            if (DemoMode)
            {
                return;
            }
            if (Input.Active && Input.CheatCombo && !NetActive)
            {
                Input.CheatCombo = false;
                CodewordRequested?.Invoke();
            }
            if (LevelGfxReady && Input.Active && (p.StateFlags & PlayerState.FlagTransforming) == 0)
            {
                if (Key(GameAction.PreviousWeapon))
                {
                    Touch();
                    for (int s = WeaponCursor - 1; s >= 0; s--)
                    {
                        if (Usable(s + 1, p.Form))
                        {
                            p.Weapon = s + 1;
                            SelectSlot(p.Weapon);
                            break;
                        }
                    }
                }
                if (Key(GameAction.NextWeapon))
                {
                    Touch();
                    for (int s = WeaponCursor + 1; s < 8; s++)
                    {
                        if (Usable(s + 1, p.Form))
                        {
                            p.Weapon = s + 1;
                            SelectSlot(p.Weapon);
                            break;
                        }
                    }
                }
                if (Key(GameAction.PreviousEquipment))
                {
                    Touch();
                    for (int s = EquipmentCursor - 1; s >= 0; s--)
                    {
                        if (Usable(ItemType.EquipmentList[s], p.Form))
                        {
                            SelectSlot(ItemType.EquipmentList[s]);
                            break;
                        }
                    }
                }
                if (Key(GameAction.NextEquipment))
                {
                    Touch();
                    for (int s = EquipmentCursor + 1; s < ItemType.EquipmentList.Length; s++)
                    {
                        if (Usable(ItemType.EquipmentList[s], p.Form))
                        {
                            SelectSlot(ItemType.EquipmentList[s]);
                            break;
                        }
                    }
                }
                if (Key(GameAction.UseEquipment))
                {
                    Touch();
                    UseEquipment(EquipmentCursor);
                }
            }
        }

        /// <summary>
        /// The direction command, form keys and facing. Turning or dying only falls; no control while transforming,
        /// transported or on a slide floor.
        /// </summary>
        private void ReadMoveKeys(PlayerState p)
        {
            if ((LevelGfxReady && PhysicsOn && (p.StateFlags & PlayerState.FlagTurning) != 0) || (p.StateFlags & PlayerState.FlagDying) != 0)
            {
                p.CmdDir = 8;
                return;
            }
            if ((p.StateFlags & 0x31) != 0)
            {
                return;
            }
            fireHeld = Input.Held(GameAction.Fire);
            firePressed = Input.PressedSincePoll(GameAction.Fire);
            if (LevelGfxReady)
            {
                int bx = p.X, bw = FormW[p.Form];
                BodyIn(ref bx, ref bw);
                if (p.Form != PlayerState.Plane && BlockedD(bx, p.Y, bw, FormH[p.Form], true, false) && OnSlideFloor(bx, bw))
                {
                    return; // No control on slide floors
                }
            }
            keyboardAllowed = true;

            if (!keyboardAllowed || DemoInput() || !Input.Active)
            {
                return;
            }
            bool horiz = false;
            if (Aimable(p.Weapon) && fireHeld)
            {
                p.MoveEnable = 0; // The direction keys aim the weapon
            }
            else
            {
                horiz = ReadDirectionKeys(p);
            }

            if (Key(GameAction.ToPlane))
            {
                Touch();
                RequestTransform(PlayerState.Plane);
            }
            else if (Key(GameAction.ToTank))
            {
                Touch();
                RequestTransform(PlayerState.Tank);
            }
            else if (Key(GameAction.ToWalker))
            {
                Touch();
                RequestTransform(PlayerState.Walker);
            }

            if (horiz)
            {
                if ((p.CmdDir & 3) != p.Facing)
                {
                    p.MoveEnable = 0; // The first frame of a reversal only turns
                }
                p.Facing = (p.CmdDir & 2) != 0 ? 2 : 1;
            }
        }

        /// <summary>The direction keys into CmdDir; true when the command has a sideways part.</summary>
        private bool ReadDirectionKeys(PlayerState p)
        {
            bool horiz = false;
            if (Key(GameAction.UpLeft))
            {
                p.CmdDir = 5;
                horiz = true;
            }
            else if (Key(GameAction.UpRight))
            {
                p.CmdDir = 6;
                horiz = true;
            }
            else if (Key(GameAction.DownLeft))
            {
                p.CmdDir = 9;
                horiz = true;
            }
            else if (Key(GameAction.DownRight))
            {
                p.CmdDir = 10;
                horiz = true;
            }
            else
            {
                if (Key(GameAction.Up))
                {
                    p.MoveEnable = 1;
                    p.CmdDir = (p.CmdDir & ~8) | 4;
                    Touch();
                }
                if (Key(GameAction.Left))
                {
                    horiz = true;
                    p.CmdDir = (p.CmdDir & ~2) | 1;
                    p.MoveEnable = 1;
                    Touch();
                }
                if (Key(GameAction.Right))
                {
                    p.CmdDir = (p.CmdDir & ~1) | 2;
                    horiz = true;
                    p.MoveEnable = 1;
                    Touch();
                }
                if (!Key(GameAction.Down))
                {
                    return horiz;
                }
                p.CmdDir = (p.CmdDir & ~4) | 8;
            }
            p.MoveEnable = 1;
            Touch();
            return horiz;
        }

        /// <summary>Moves the Weapons / Equipment window cursor to an item id.</summary>
        public void SelectSlot(int id)
        {
            if (id < 0x11)
            {
                int sel = Math.Max(id - 1, 0);
                WeaponCursor = sel;
                if (sel < WeaponListFirst)
                {
                    WeaponListFirst = Math.Max(sel - (sel & 1), 0);
                }
                else if (sel >= WeaponListFirst + 8)
                {
                    WeaponListFirst = Math.Min(sel - (sel & 1) - 6, 8);
                }
            }
            else
            {
                int idx = Array.IndexOf(ItemType.EquipmentList, id);
                if (idx < 0)
                {
                    return;
                }
                if (idx < EquipmentListFirst)
                {
                    EquipmentListFirst = Math.Max(idx - (idx & 1), 0);
                }
                else if (idx >= EquipmentListFirst + 8)
                {
                    EquipmentListFirst = Math.Min(idx - (idx & 1) - 6, 8);
                }
                EquipmentCursor = idx;
            }
            Sounds.Play(0x44);
        }

        /// <summary>Switches on the selected piece of equipment.</summary>
        public void UseEquipment(int listIndex)
        {
            if (listIndex < 0 || listIndex >= ItemType.EquipmentList.Length)
            {
                return;
            }
            int t = ItemType.EquipmentList[listIndex];
            if (Inv.Owned[t] != 0 && Inv.Count[t] != 0 && Inv.Active[t] == 0 && Items[t].Timer != 0)
            {
                Inv.Active[t] = Items[t].Timer;
                ItemEffect(t, on: true);
            }
        }

        /// <summary>Weapon swap, bomb detonation, firing with auto-aim.</summary>
        private void PlayerFire()
        {
            var p = Player;
            int aimDir = 0;
            if (p.Busy)
            {
                return;
            }

            if (Input.PressedSincePoll(GameAction.SwapWeapons) && p.Form == PlayerState.Walker)
            {
                (WeaponCursor, AltWeaponCursor) = (AltWeaponCursor, WeaponCursor);
                (p.Weapon, p.AltWeapon) = (p.AltWeapon, p.Weapon);
            }
            if (Key(GameAction.Detonate) && LevelGfxReady && SessionActive && p.PlayState == 0)
            {
                int best = -1, bestFuse = 0x7fff;
                for (int i = 0; i < Timed.Length; i++)
                {
                    var o = Timed[i];
                    if (o.Active != 0 && (o.Type == 6 || o.Type == 7) && o.Fuse != 0 && o.Fuse < bestFuse)
                    {
                        bestFuse = o.Fuse;
                        best = i;
                    }
                }
                if (best >= 0)
                {
                    Timed[best].Fuse = 1;
                    Timed[best].Triggered = 1;
                }
            }

            // Fire only on odd frame numbers: at most one shot every 2 frames.
            bool keyFire = fireHeld && (FrameCounter % 2 != 0);
            if (keyFire && Aimable(p.Weapon))
            {
                aimDir = Key(GameAction.Up) ? 4
                    : Key(GameAction.UpRight) ? 6
                    : Key(GameAction.Right) ? 2
                    : Key(GameAction.DownRight) ? 10
                    : Key(GameAction.Down) ? 8
                    : Key(GameAction.DownLeft) ? 9
                    : Key(GameAction.Left) ? 1
                    : Key(GameAction.UpLeft) ? 5 : 0;
                if (aimDir == 0)
                {
                    keyFire = false;
                }
                if ((p.Facing == 2 && (aimDir & 1) != 0) || (p.Facing == 1 && (aimDir & 2) != 0))
                {
                    keyFire = false; // No shooting backwards
                }
            }
            if (!(keyFire && Input.Active) && !DemoFire())
            {
                return;
            }

            Touch();
            FireWeapon(p, aimDir);
            if (Inv.Count[p.Weapon] == 0 && (firePressed || DemoMode))
            {
                AutoSwitchWeapon();
            }
        }

        /// <summary>Spends a round and launches it: bombs drop, auto-aim weapons pick a target, the rest fly where aimed.</summary>
        private void FireWeapon(PlayerState p, int aimDir)
        {
            if (!Usable(p.Weapon, p.Form))
            {
                return;
            }
            if (Inv.Count[p.Weapon] == 0)
            {
                Sounds.Play(0x5f);
                return;
            }
            if (Inv.Count[p.Weapon] == 10)
            {
                Sounds.Play(0x98);
            }
            Inv.Count[p.Weapon]--;
            if (p.Weapon == 6 || p.Weapon == 7)
            {
                DropBomb(p.Weapon);
                return;
            }

            int camX = CamX, camY = CamY;
            int viewW = ViewWidthCells, viewH = ViewHeightCells;
            int scanL, scanR;
            if ((p.Form == PlayerState.Plane && (p.LastDir & 6) != 0) || (p.Form != PlayerState.Plane && p.Facing == 2))
            {
                scanL = p.X + FormW[p.Form] - 1;
                scanR = camX + viewW;
            }
            else
            {
                scanL = camX;
                scanR = p.X;
            }
            if (p.Form == PlayerState.Walker)
            {
                if (p.SpriteSet < 6)
                {
                    if (!Aimable(p.Weapon))
                    {
                        p.Frame += 13; // The same frame with the gun arm out
                    }
                    else
                    {
                        p.SpriteSet = p.Facing == 2 ? 0x19 : 0x18;
                        p.Frame = aimDir switch
                        {
                            1 or 2 => 6,
                            4 => 0,
                            5 or 6 => 3,
                            8 => 12,
                            9 or 10 => 9,
                            _ => p.Frame,
                        };
                    }
                }
                else if (p.SpriteSet == 0x26 || p.SpriteSet == 0x29)
                {
                    p.Frame = 9; // Airborne firing frame
                }
            }
            var (mx, my) = Muzzle(p, p.Weapon, camX, camY);
            int tx = 0, ty = 0;

            if ((p.Form == PlayerState.Walker && !Aimable(p.Weapon)) || (p.Form == PlayerState.Plane && p.Weapon == 8))
            {
                if (FireAtTargetInFront(p, mx, my, scanL, scanR, camX, camY, viewH))
                {
                    return;
                }

                // No enemy: the next shoot-away cell in front ("secret entrances"), else straight ahead.
                if (!TryShootAwayCell(p, mx, my, camX, camY, viewW, viewH, out tx, out ty))
                {
                    tx = p.Facing == 2 ? ViewWidthPx : 0;
                    ty = my;
                    if (p.Form == PlayerState.Plane)
                    {
                        AimAlong(p.LastDir, mx, my, ref tx, ref ty);
                    }
                }
            }
            else if (p.Form == PlayerState.Plane)
            {
                AimAlong(p.LastDir, mx, my, ref tx, ref ty);
            }
            else if (p.Form != PlayerState.Walker || !Aimable(p.Weapon))
            {
                // Tank with a non-bomb weapon: unreachable with the stock item table.
                tx = scanL;
                ty = scanR;
            }
            else
            {
                AimAlong(aimDir, mx, my, ref tx, ref ty);
            }
            FireEvent(p.Weapon, 0x7f, 0, S * camX + tx, S * camY + ty);
        }

        /// <summary>Auto-aim at a target in front within the view; true when it fired.</summary>
        private bool FireAtTargetInFront(PlayerState p, int mx, int my, int scanL, int scanR, int camX, int camY, int viewH)
        {
            // Network play: the other players first.
            if (NetActive && NetAutoAim(p.Weapon, scanL, scanR, camY, viewH))
            {
                return true;
            }

            // The next living enemy (round robin).
            int start = lastTarget;
            do
            {
                if (++lastTarget > 99)
                {
                    lastTarget = 1;
                }
                var t = Enemies[lastTarget];
                if (t.State == EnemyRecord.Alive && scanL < t.X + t.W && camY < t.Y + t.H && t.X < scanR && t.Y < camY + viewH
                    && (!SmartAiming || LineOfFire(mx, my, camX, camY, t.X, t.Y, t.W, t.H)))
                {
                    FireEvent(p.Weapon, 0x7f, lastTarget, 0, 0);
                    return true;
                }
            }
            while (lastTarget != start);
            if (--lastTarget == 0)
            {
                lastTarget = 99;
            }
            return false;
        }

        /// <summary>
        /// The next shoot-away cell in front within the view, column by column from where the last scan stopped;
        /// the target in view pixels.
        /// </summary>
        private bool TryShootAwayCell(PlayerState p, int mx, int my, int camX, int camY, int viewW, int viewH, out int tx, out int ty)
        {
            int facing = p.Facing;
            bool firstPass = true;
            int off = 0;
            if (scanY < camY || camY + viewH <= scanY || (short)scanX < camX || camX + viewW <= (short)scanX)
            {
                scanX = p.X;
                scanY = camY;
                if (facing == 2)
                {
                    scanX = p.X + 4;
                }
            }
            int sx = scanX, sy = scanY;
            for (int guard = 0; guard < 100000; guard++)
            {
                if (facing == 2)
                {
                    off = 4;
                }
                sy++;
                if (scanFacing != facing)
                {
                    sx = p.X + off;
                    scanFacing = facing;
                    sy = camY;
                    scanX = sx;
                    scanY = camY;
                }
                if (camY + viewH <= sy)
                {
                    sx += facing == 2 ? 1 : -1;
                    sy = camY;
                    if (camX + viewW <= sx || sx < camX)
                    {
                        sx = p.X + off;
                        sy = camY;
                    }
                }
                bool bad = facing == 2
                    ? (sx < p.X + off || camX + viewW <= sx || sy < camY || camY + viewH < sy)
                    : (sx < camX || p.X < sx || sy < camY || camY + viewH <= sy);
                if (bad)
                {
                    sx = p.X + off;
                    scanFacing = facing;
                    sy = camY;
                    scanX = sx;
                    scanY = camY;
                }
                for (; sy < camY + viewH; sy++)
                {
                    int idx = sy * MapW + sx;
                    if (idx >= 0 && idx < Map.Count && (Map.B2(idx) & 0x80) != 0
                        && (!SmartAiming || (Map.Tile(idx) != 0 && LineOfFire(mx, my, camX, camY, sx, sy, 1, 1))))
                    {
                        tx = (sx - camX) * S + 2;
                        ty = (sy - camY) * S + 2;
                        scanFacing = facing;
                        scanX = sx;
                        scanY = sy;
                        return true;
                    }
                }
                sy = firstPass || sx != scanX ? camY + viewH - 1 : scanY;
                if (sx == scanX && sy == scanY)
                {
                    break; // A full cycle: nothing found
                }
                firstPass = false;
            }
            tx = 0;
            ty = 0;
            return false;
        }

        /// <summary>The view edge or corner a shot in <paramref name="dir"/> heads for; other values keep the target.</summary>
        private void AimAlong(int dir, int mx, int my, ref int tx, ref int ty)
        {
            switch (dir)
            {
                case 1: tx = 0; ty = my; break;
                case 2: tx = ViewWidthPx; ty = my; break;
                case 4: tx = mx; ty = 0; break;
                case 5: tx = 0; ty = 0; break;
                case 6: tx = ViewWidthPx; ty = 0; break;
                case 8: tx = mx; ty = ViewHeightPx; break;
                case 9: tx = 0; ty = ViewHeightPx; break;
                case 10: tx = ViewWidthPx; ty = ViewHeightPx; break;
            }
        }

        /// <summary>After firing the last round of a weapon.</summary>
        private void AutoSwitchWeapon()
        {
            var p = Player;
            int w0 = p.Weapon, a0 = p.AltWeapon;
            int found = -1;
            for (int t = 1; t <= 16; t++)
            {
                if (Usable(t, p.Form) && !Aimable(t) && Inv.Count[t] != 0)
                {
                    found = t;
                    break;
                }
            }
            if (p.Form == PlayerState.Walker && p.AltWeapon != 0 && Inv.Count[p.AltWeapon] != 0)
            {
                p.Weapon = p.AltWeapon;
                p.AltWeapon = w0;
                SelectSlot(a0);
            }
            else if (found >= 0)
            {
                p.Weapon = found;
                SelectSlot(found);
            }
        }

        /// <summary>The muzzle point in view-relative pixels.</summary>
        private (int X, int Y) Muzzle(PlayerState p, int weapon, int camX, int camY)
        {
            int dx = (p.X - camX) * S, dy = (p.Y - camY) * S;
            int w = FormW[p.Form] * S;
            int half = (int)(S * 2.5), threeHalf = (int)(S * 3.5), sixHalf = (int)(S * 6.5);
            if (p.SpriteSet == 1 || p.SpriteSet == 4)
            {
                return (p.Frame / 2) switch
                {
                    0 => (dx + 5 * S, dy + half),
                    1 => (dx + 4 * S, dy + S),
                    2 => (dx + half, dy),
                    3 => (dx + S, dy + S),
                    4 => (dx, dy + half),
                    5 => (dx + S, dy + 4 * S),
                    6 => (dx + half, dy + 5 * S),
                    7 => (dx + 4 * S, dy + 4 * S),
                    _ => (0, 0),
                };
            }
            int set = p.SpriteSet;
            bool left = set is 0 or 1 or 2 or 0x18 or (>= 0x1d and <= 0x1f) or (>= 0x26 and <= 0x28);
            int mx = left ? dx + 2 : dx + w - 2;
            int my = dy;
            if (!Aimable(weapon))
            {
                return (mx, p.Frame <= 12 ? dy + half : dy + 2 * S);
            }
            if (p.Frame > 14)
            {
                return (mx, dy + 2 * S);
            }
            switch (p.Frame / 3)
            {
                case 0:
                    mx += left ? S : -S;
                    my = dy + (S >> 1);
                    break;
                case 1:
                    break;
                case 2:
                    mx += left ? -(S >> 1) : (S >> 1);
                    my = dy + 2 * S;
                    break;
                case 3:
                    my = dy + threeHalf;
                    break;
                case 4:
                    mx += left ? 2 * S : -2 * S;
                    my = dy + sixHalf;
                    break;
            }
            return (mx, my);
        }

        /// <summary>The front-bottom 2 x 2 corner of the player's box.</summary>
        private void DropBomb(int type)
        {
            var p = Player;
            int x = p.Facing != 1 ? p.X + FormW[p.Form] - 2 : p.X;
            int y = p.Y + FormH[p.Form] - 2;
            SpawnBomb(x, y, type);
            if (NetActive)
            {
                NetQueueBomb(x, y, type);
            }
        }
    }
}
