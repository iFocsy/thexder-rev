namespace Thexder.Core.Game
{
    // Shots, explosions, area blasts and bombs.
    internal sealed partial class ThexderGame
    {
        // Enemy muzzle tables by direction.
        private static readonly int[] MuzzleDx16 = { 2, 3, 4, 4, 4, 4, 3, 2, 1, 0, 0, 0, 0, 0, 0, 1 };
        private static readonly int[] MuzzleDy16 = { 4, 4, 4, 3, 2, 1, 0, 0, 0, 0, 0, 1, 2, 3, 4, 4 };
        private static readonly int[] DragonDx = { 6, 6, 10, 10, 12, 12, 10, 10, 6, 6, 2, 2, 0, 0, 2, 2 };
        private static readonly int[] DragonDy = { 12, 12, 10, 10, 6, 6, 2, 2, 0, 0, 2, 2, 6, 6, 10, 10 };
        private static readonly int[] FlyerDx = { 8, 8, 7, 7, 4, 4, 2, 2, 0, 0, 1, 1, 4, 4, 7, 7 };
        private static readonly int[] FlyerDy = { 3, 3, 2, 2, 0, 0, 2, 2, 3, 3, 6, 6, 8, 8, 6, 6 };
        private static readonly int[] WalkerDx = { 4, 2, 1, 2, 4 };
        private static readonly int[] WalkerDy = { 1, 0, 3, 5, 6 };

        private static readonly int[] MissileOffX = { 8, 16, 16, 16, 8, 0, 0, 0 };
        private static readonly int[] MissileOffY = { 0, 0, 8, 16, 16, 16, 8, 0 };

        // Fan spread multipliers.
        private static readonly int[] FanMultiplier = { 4, 2 };

        private int missCounter;

        /// <summary>Fires a shot type from the player (owner 0x7F) or a enemy.</summary>
        public void FireEvent(int type, int owner, int enemyTarget, int x, int y)
        {
            if (type < 0 || type >= 0x29 || !ShotTypes[type].Defined)
            {
                return;
            }
            // The request packet stores owner and target as bytes and the coordinates as shorts.
            owner &= 0xff;
            enemyTarget &= 0xff;
            if (NetActive && !NetQueueFire(type, owner, enemyTarget, (short)x, (short)y))
            {
                return; // Network play: the event queue is full
            }
            ExecuteShot(Player, CamX, CamY, type, owner, owner, enemyTarget, (short)x, (short)y, NetLocalId);
        }

        /// <summary>
        /// ExecuteFire: <paramref name="shooter"/> is the player (0x7F, whose record and camera are given: the local
        /// player or another player in network play) or an enemy; <paramref name="owner"/> is what the shot records (0x7E
        /// for another player's shot).
        /// </summary>
        private void ExecuteShot(PlayerState rec, int camX, int camY, int type, int shooter, int owner, int enemyTarget, int x, int y, int netId)
        {
            int speed = ShotTypes[type].Speed;
            var (ox, oy, tx, ty, damage) = ShotOrigin(rec, camX, camY, type, shooter, enemyTarget, x, y);
            FireSpread(type, ox, oy, tx, ty, damage, owner, speed, netId);
        }

        /// <summary>Muzzle point, target and damage of a shot.</summary>
        private (int Ox, int Oy, int Tx, int Ty, int Damage) ShotOrigin(PlayerState rec, int camX, int camY, int type, int owner, int enemyTarget, int reqX, int reqY)
        {
            int viewW = ViewWidthPx, viewH = ViewHeightPx;
            int ox, oy, tx, ty, damage = 0;
            if (owner == 0x7f)
            {
                damage = Items[type].Damage;
                var (mx, my) = Muzzle(rec, type, camX, camY);
                ox = (short)(mx + S * camX);
                oy = (short)(my + S * camY);
                if (enemyTarget == 0)
                {
                    tx = reqX;
                    ty = reqY;
                }
                else
                {
                    // A random point in the middle of the target enemy.
                    var t = Enemies[enemyTarget];
                    var (tcx, tcy) = EnemyCell(enemyTarget);
                    int w6 = t.W * 6, h6 = t.H * 6;
                    int rx = w6 != 0 ? Random.Next() % w6 : 0;
                    int ry = h6 != 0 ? Random.Next() % h6 : 0;
                    tx = (short)((tcx - camX) * S - (w6 >> 1) + t.W * 4 + rx);
                    ty = (short)((tcy - camY) * S - (h6 >> 1) + t.H * 4 + ry);
                    tx = Math.Max(0, Math.Min(tx, viewW));
                    ty = Math.Max(0, Math.Min(ty, viewH));
                    tx += S * camX;
                    ty += S * camY;
                }
                return (ox, oy, tx, ty, damage);
            }

            var r = Enemies[owner];
            var tt = TypeOf(r);
            if (tt != null)
            {
                foreach (var weapon in tt.Weapons)
                {
                    if ((sbyte)weapon.Shot == (sbyte)type)
                    {
                        damage = weapon.Dmg;
                        break;
                    }
                }
            }
            var (cx, cy) = EnemyCell(owner);
            int dir = r.Frame;
            int sel = 0;
            switch (r.Type)
            {
                case 1:
                    ox = (MuzzleDx16[dir & 15] + cx) * S;
                    oy = (MuzzleDy16[dir & 15] + cy) * S;
                    break;
                case 0x14:
                    sel = dir;
                    ox = (DragonDx[dir & 15] + cx) * S;
                    oy = (DragonDy[dir & 15] + cy) * S;
                    break;
                case 0x1d:
                case 0x31:
                    ox = reqX;
                    oy = reqY;
                    break;
                case 0x27:
                    sel = (dir * 2) & 0xffff;
                    ox = (MuzzleDx16[sel & 15] * 2 + cx) * S;
                    oy = (MuzzleDy16[sel & 15] * 2 + cy) * S;
                    break;
                case 0x29:
                    ox = (FlyerDx[dir & 15] + cx) * S;
                    oy = (FlyerDy[dir & 15] + cy) * S;
                    break;
                case 0x2a:
                {
                    int k = Math.Min(dir / 3, WalkerDx.Length - 1);
                    sel = k;
                    if ((r.Flags & EnemyRecord.FlagFacingLeft) == 0)
                    {
                        ox = (r.W - WalkerDx[k] + cx) * S;
                        oy = (r.H - WalkerDy[k] + cy) * S;
                    }
                    else
                    {
                        ox = (WalkerDx[k] + cx) * S;
                        oy = (WalkerDy[k] + cy) * S;
                    }
                    break;
                }
                default:
                {
                    int dx = tt?.ShotDX ?? 0, dy = tt?.ShotDY ?? 0;
                    ox = (r.Flags & EnemyRecord.FlagFacingLeft) != 0 ? S * cx + dx : (cx + r.W) * S - dx;
                    oy = S * cy + dy;
                    break;
                }
            }

            if (enemyTarget == 0)
            {
                if (r.Type == 0x14 || r.Type == 0x27)
                {
                    tx = S * camX;
                    ty = S * camY;
                    switch (sel)
                    {
                        case 0: case 1: ty += viewH; tx = ox; break;
                        case 2: case 3: ty += viewH; tx += viewW; break;
                        case 4: case 5: tx += viewW; ty = oy; break;
                        case 6: case 7: tx += viewW; break;
                        case 8: case 9: tx = ox; break;
                        case 12: case 13: ty = oy; break;
                        case 14: case 15: ty += viewH; break;
                    }
                }
                else if (r.Type == 0x2a)
                {
                    bool left = (r.Flags & EnemyRecord.FlagFacingLeft) != 0;
                    tx = 0;
                    ty = 0;
                    switch (r.Frame)
                    {
                        case 0: case 1: case 2: tx = ox; ty = 0; break;
                        case 3: case 4: case 5: tx = left ? 0 : viewW; ty = 0; break;
                        case 6: case 7: case 8: tx = left ? 0 : viewW; ty = oy; break;
                        case 9: case 10: case 11: tx = left ? 0 : viewW; ty = viewH; break;
                        case 12: case 13: case 14: tx = ox; ty = viewH; break;
                    }
                }
                else
                {
                    tx = reqX;
                    ty = reqY;
                }
            }
            else
            {
                // Aim at the player (in network play, the player whose machine fired it).
                var p = rec;
                int px = r.Type == 1 ? (FormW[p.Form] >> 1) + p.X
                    : (r.Flags & EnemyRecord.FlagFacingLeft) == 0 ? FormW[p.Form] + p.X : p.X;
                tx = px * S;
                ty = ((FormH[p.Form] >> 1) + p.Y) * S;
            }
            return ((short)ox, (short)oy, (short)tx, (short)ty, damage);
        }

        /// <summary>The shot, plus the fan beams (8, 0x22) or Tri-turret barrels (0x1e).</summary>
        private void FireSpread(int type, int x0, int y0, int x1, int y1, int damage, int owner, int speed, int netId = 0)
        {
            SpawnShot(type, x0, y0, x1, y1, damage, owner, speed, netId);
            if (type != 8 && type != 0x22 && type != 0x1e)
            {
                return;
            }
            int ax = Math.Abs(x1 - x0) / S;
            int ay = Math.Abs(y1 - y0) / S;
            int pairs = type == 0x22 ? 2 : 1;
            for (int k = 0; k < pairs; k++)
            {
                int m = FanMultiplier[k];
                int xa, xb, ya, yb;
                if (y1 == y0)
                {
                    xa = xb = x1;
                    ya = y1 + m * ax;
                    yb = y1 - m * ax;
                }
                else if (x1 != x0)
                {
                    int sa, sb;
                    if ((y0 < y1 || x1 < x0) && (y1 < y0 || x0 < x1))
                    {
                        sa = m * ay;
                        sb = -sa;
                    }
                    else
                    {
                        sb = m * ay;
                        sa = -sb;
                    }
                    xa = x1 + sa;
                    xb = x1 + sb;
                    ya = y1 - m * ax;
                    yb = y1 + m * ax;
                }
                else
                {
                    xa = x1 + m * ay;
                    xb = x1 - m * ay;
                    ya = yb = y1;
                }

                int bx = x0, by = y0;
                if (type == 0x1e)
                {
                    // The two extra missiles leave from the neighbouring barrels.
                    var t = Enemies[owner];
                    int d = t.Frame & 0xffff;
                    int prev = d != 0 ? d - 1 : 7;
                    int next = d < 7 ? d + 1 : 0;
                    int l, u;
                    if (d is 2 or 4 or 5 or 7)
                    {
                        u = prev;
                        l = next;
                    }
                    else
                    {
                        l = prev;
                        u = next;
                    }
                    bx = (t.X + MuzzleDx16[(l * 2) & 15] * 2) * S;
                    by = (t.Y + MuzzleDy16[(l * 2) & 15] * 2) * S;
                    SpawnShot(0x1e, (t.X + MuzzleDx16[(u * 2) & 15] * 2) * S, (t.Y + MuzzleDy16[(u * 2) & 15] * 2) * S, xa, ya, damage, owner, speed, netId);
                }
                else
                {
                    SpawnShot(type, x0, y0, xa, ya, damage, owner, speed, netId);
                }
                SpawnShot(type, bx, by, xb, yb, damage, owner, speed, netId);
            }
        }

        /// <summary>Takes the first free shot slot.</summary>
        private void SpawnShot(int type, int x0, int y0, int x1, int y1, int damage, int owner, int speed, int netId = 0)
        {
            ShotRecord? s = null;
            foreach (var candidate in Shots)
            {
                if (candidate.State == 0)
                {
                    s = candidate;
                    break;
                }
            }
            if (s == null)
            {
                return;
            }
            var t = ShotTypes[type];
            s.State = 1;
            s.ViewBusy = 0;
            s.Type = type;
            s.Reserved = 0;
            s.Owner = owner;
            s.Damage = damage;
            s.ExplVariant = t.Explosions.Length == 0 ? 0 : Random.Next() % t.Explosions.Length;
            s.X0 = s.X = x0;
            s.Y0 = s.Y = y0;
            s.X1 = x1;
            s.Y1 = y1;
            s.Yf = y0;
            s.FaceRight = 0;
            s.ArcCounter = 0;
            s.HitTarget = 0;
            s.HitEnemy = -1;
            s.NetCounter = 0;
            s.Speed = speed;
            s.Frame = 0;
            s.NetPlayer = netId;
            s.Serial = ++shotSerial;
            switch (type)
            {
                case 2: case 3: case 4: case 5: case 0x12: case 0x13: case 0x14: case 0x1b: case 0x1c:
                case 0x1d: case 0x1e: case 0x1f: case 0x20: case 0x21: case 0x24:
                {
                    int adx = Math.Abs(x1 - x0);
                    if (adx == 0)
                    {
                        adx = 1;
                    }
                    float slope = (float)(y1 - y0) / adx;
                    s.Dy = slope;
                    s.Dx = x1 < x0 ? -1 : x0 < x1 ? 1 : 0;
                    if (s.Dx == 0)
                    {
                        s.Dy = slope > 0 ? 1.0f : -1.0f;
                    }
                    break;
                }
                case >= 0x15 and <= 0x1a:
                    // Rubble falls straight down from the target point.
                    s.X0 = s.X1 = x1;
                    s.Y0 = y1;
                    s.Y1 = ViewHeightPx + y1;
                    s.Dx = 0;
                    s.Dy = 1.0f;
                    break;
            }
            if (ShotTypes_IsDirectional(type))
            {
                s.Frame = Direction8(s.Dx, s.Dy);
            }
            if (t.FlySpriteR != null && x0 < x1)
            {
                s.FaceRight = 1;
            }
        }

        private static bool ShotTypes_IsDirectional(int type) => Game.ShotTypes.IsDirectionalMissile(type);

        private static int Direction8(int dx, float dy)
        {
            if (dx == 0)
            {
                return dy == -1 ? 0 : 4;
            }
            if (dx == -1)
            {
                return dy == 0 ? 6 : dy > 0 ? 5 : 7;
            }
            return dy == 0 ? 2 : dy > 0 ? 3 : 1;
        }

        private void ClearShots()
        {
            foreach (var s in Shots)
            {
                s.Reset();
            }
        }

        private void UpdateShots()
        {
            for (int i = 0; i < Shots.Length; i++)
            {
                if (Shots[i].State != 0)
                {
                    SceneTag(EntityShot | i, ShotOwnerEntity(Shots[i]));
                    UpdateShot(Shots[i]);
                }
            }
            SceneTag(0);
        }

        private void UpdateShot(ShotRecord s)
        {
            var t = ShotTypes[s.Type];
            bool right = s.X0 < s.X1;
            int pan = right ? 1000 : -1000;
            var identity = Art.Tints.Identity;
            if (s.State == 2)
            {
                if (s.ExplVariant < t.Explosions.Length)
                {
                    var e = t.Explosions[s.ExplVariant];
                    if (s.Frame == 0 && e.Sound != 0xffff)
                    {
                        Sounds.PlayAt(e.Sound, pan, -800);
                    }
                    DrawSpritePixels(Art.Sprite(e.Sprite), Math.Max(0, s.X - e.XOff), Math.Max(0, s.Y - e.YOff), e.W, e.H, s.Frame, identity);
                    if (++s.Frame >= e.Frames)
                    {
                        s.State = 0;
                    }
                }
                else
                {
                    s.State = 0;
                }
                return;
            }
            if (s.State != 1)
            {
                return;
            }

            bool hit = true;
            if (s.X == s.X0 && s.Y == s.Y0)
            {
                if (t.FireSound != 0xffff)
                {
                    Sounds.PlayAt(t.FireSound, pan, -800);
                }
                if (t.MuzzleFrames != 0)
                {
                    int mx = s.X0 - t.MuzzleW / 2;
                    if (!right)
                    {
                        mx = Math.Max(0, mx);
                    }
                    DrawSpritePixels(Art.Sprite(t.MuzzleSprite), mx, s.Y0 - t.MuzzleH / 2, t.MuzzleW, t.MuzzleH, 0, identity);
                }
            }
            if (Game.ShotTypes.IsHitscan(s.Type))
            {
                TraceLine(s, destroy: false);
            }
            else if (Game.ShotTypes.IsArc(s.Type))
            {
                hit = MoveProjectile(s, arc: true, trail: false);
            }
            else if (Game.ShotTypes.IsTrail(s.Type))
            {
                hit = MoveProjectile(s, arc: false, trail: true);
            }
            else
            {
                hit = MoveProjectile(s, arc: false, trail: false);
            }
            if (!hit)
            {
                return;
            }

            if (t.ImpactSound != 0xffff)
            {
                Sounds.PlayAt(t.ImpactSound, pan, -800);
            }
            if (t.BlastRadius != 0)
            {
                AreaBlast(s.X / S, s.Y / S, t.BlastRadius, s.Damage, hitEnemies: false, hitPlayer: s.Type > 16);
            }
            if (s.HitEnemy < 1)
            {
                if (s.Owner != 0x7f)
                {
                    if (s.HitTarget == 0x7f)
                    {
                        if (s.Owner != 0x35)
                        {
                            DamagePlayer(s.Damage, false);
                        }
                        else if (Inv.IsActive(0x19) || Inv.IsActive(0x18))
                        {
                            DamagePlayer(s.Damage / 5, false);
                        }
                        else if (!Inv.IsActive(ItemType.MegaArmor))
                        {
                            DamagePlayer(s.Damage, false);
                        }
                    }
                    return;
                }
                if (s.HitTarget == 0x7e)
                {
                    Player.Score += (uint)s.Damage;
                    return;
                }
                if (++missCounter > 15)
                {
                    missCounter = 0;
                    Sounds.Play(0xa1 + Random.Next() % 4); // DONTSHOT, DONTWAST, NOTPRDNT, STATPOOR
                }
            }
            else if (EnemyCanBeHitBy(s.HitEnemy, s.Type))
            {
                EnemyDamage(s.HitEnemy, s.Damage);
            }
        }

        /// <summary>
        /// A Bresenham line from the muzzle that inverts the screen pixels it passes (the laser beam) and stops at the
        /// first thing it hits.
        /// </summary>
        private void TraceLine(ShotRecord s, bool destroy)
        {
            bool stoppedByHit = false;
            bool Plot(int px, int py)
            {
                bool go = PlotPixel(px, py, destroy, s);
                stoppedByHit |= !go;
                return go;
            }
            int x = s.X0, y = s.Y0, ex = s.X1, ey = s.Y1;
            int ddx = ex - x, ddy = ey - y;
            int ax = 2 * Math.Abs(ddx), ay = 2 * Math.Abs(ddy);
            int sx = Math.Sign(ddx), sy = Math.Sign(ddy);
            if (ay < ax)
            {
                int err = -(ax >> 1);
                while (true)
                {
                    err += ay;
                    if (!Plot(x, y) || x == ex)
                    {
                        break;
                    }
                    if (err >= 0)
                    {
                        y += sy;
                        err -= 2 * Math.Abs(ddx);
                    }
                    x += sx;
                }
            }
            else
            {
                int err = -(ay >> 1);
                while (true)
                {
                    err += ax;
                    if (!Plot(x, y) || y == ey)
                    {
                        break;
                    }
                    if (err >= 0)
                    {
                        x += sx;
                        err -= 2 * Math.Abs(ddy);
                    }
                    y += sy;
                }
            }
            // The beam ran from (X0, Y0) towards (X1, Y1) and stopped at (x, y): drawn there unless it hit something.
            SceneAdd(new SceneOp(SceneOpKind.Laser, 0, 0, null, s.X0, s.Y0, x, y, stoppedByHit ? 1 : 0, null, X1: s.X1, Y1: s.Y1));
            s.X = x;
            s.Y = y;
        }

        /// <summary>Returns false to stop the beam.</summary>
        private bool PlotPixel(int x, int y, bool destroy, ShotRecord s)
        {
            if (x > S * MapW || y > S * MapH || x < 0 || y < 0)
            {
                return false;
            }
            if (CellCollide(x, y, destroy, s))
            {
                return false;
            }
            int px = x - CamX * S, py = y - CamY * S;
            if (px >= 0 && px < ViewWidthPx && py >= 0 && py < ViewHeightPx)
            {
                int i = py * Screen.Width + px;
                Screen.Pixels[i] = (byte)~Screen.Pixels[i];
            }
            return true;
        }

        /// <summary>Walls, shoot-away cells (destroyed when <paramref name="destroy"/>), enemies and the player.</summary>
        private bool CellCollide(int x, int y, bool destroy, ShotRecord s)
        {
            int cx = x / S, cy = y / S;
            int idx = Map.Wrap16(cy * MapW + cx);
            if (idx >= Map.Count)
            {
                return false;
            }
            bool free = true;
            if (Map.Tile(idx) != 0)
            {
                if ((Map.B2(idx) & 0x80) != 0)
                {
                    missCounter = 0;
                    if (destroy)
                    {
                        if (Map.Tile(idx) == 0x8a)
                        {
                            Sounds.PlayPositional(0x32, cx, cy, Player.X, Player.Y);
                        }
                        Map.SetTile(idx, 0);
                        Map.SetB2(idx, (byte)(Map.B2(idx) & 0x7f));
                    }
                    free = false;
                }
                else if ((Map.B3(idx) & 0xc0) == 0)
                {
                    free = false;
                }
            }
            int occ = Map.B2(idx) & 0x7f;
            if (free && occ != 0)
            {
                if (occ == 0x7f || occ == 0x7e || occ == s.Owner)
                {
                    if (((s.Owner != 0x7f && occ == 0x7f) || (s.Owner != 0x7e && occ == 0x7e)) && (occ == 0x7e || PlayerPixelHit(x, y)))
                    {
                        free = false;
                        s.HitTarget = occ;
                        if (occ == 0x7e)
                        {
                            missCounter = 0;
                        }
                    }
                }
                else if (occ < Enemies.Length)
                {
                    var t = Enemies[occ];
                    if (t.State == EnemyRecord.Alive || (t.Type == 6 && (t.Flags & 0x20) != 0) || (t.State == EnemyRecord.Dying && (t.Flags & 0x20) != 0))
                    {
                        free = false;
                        s.HitEnemy = occ;
                        missCounter = 0;
                    }
                }
            }
            return !free;
        }

        /// <summary>Moves, draws and collides a projectile; true when it hit.</summary>
        private bool MoveProjectile(ShotRecord s, bool arc, bool trail)
        {
            var t = ShotTypes[s.Type];
            bool hit = true;
            int x = s.X;
            float yf = s.Yf;
            if (arc && (++s.ArcCounter & 0xffff) % 6 == 0)
            {
                if (s.Dy == 1.0f)
                {
                    if (s.Dx != 0)
                    {
                        s.Dx = 0; // Falling diagonally -> fall straight down
                    }
                }
                else
                {
                    s.Dy = s.Dy == -1.0f ? 0.0f : 1.0f;
                }
            }
            int steps = 0;
            do
            {
                yf += s.Dy;
                x += s.Dx;
                if (x > S * MapW || yf > S * MapH || x < 0 || yf < 0)
                {
                    EndFlight(s, t);
                    return hit; // Left the map: a hit only on the first step
                }
                hit = CellCollide(x, (int)yf, destroy: true, s);
                steps++;
            }
            while (steps < s.Speed && !hit);

            if (!hit || s.HitTarget != 0)
            {
                int px, py;
                if (Game.ShotTypes.IsDirectionalMissile(s.Type))
                {
                    px = Math.Max(0, x - MissileOffX[s.Frame & 7]);
                    py = Math.Max(0, (int)yf - MissileOffY[s.Frame & 7]);
                }
                else
                {
                    px = x - t.FlyW / 2;
                    if (px < 1)
                    {
                        px = 0;
                    }
                    py = (int)yf - t.FlyH / 2;
                    if (py < 1)
                    {
                        py = 0;
                    }
                }
                var sprite = Art.Sprite(s.FaceRight != 0 ? t.FlySpriteR : t.FlySpriteL);
                DrawSpritePixels(sprite, px, py, t.FlyW, t.FlyH, s.Frame, Art.Tints.Identity);
                if (trail)
                {
                    for (int k = s.Frame; k != 0; k--)
                    {
                        px += s.Dx < 1 ? t.FlyW : -t.FlyW;
                        DrawSpritePixels(sprite, px, py, t.FlyW, t.FlyH, k - 1, Art.Tints.Identity);
                    }
                }
                if (t.FlyFrames > 1 && ++s.Frame >= t.FlyFrames)
                {
                    s.Frame = trail ? t.FlyFrames - 1 : 0;
                }
            }
            s.X = x;
            s.Y = (short)(int)yf;
            s.Yf = yf;
            if (!hit)
            {
                return false;
            }
            EndFlight(s, t);
            return true;
        }

        /// <summary>A flying shot that stopped: it explodes where it is, or ends at once without explosion frames.</summary>
        private static void EndFlight(ShotRecord s, ShotType t)
        {
            if (s.State == 1)
            {
                if (t.Explosions.Length == 0)
                {
                    s.State = 0;
                }
                else
                {
                    s.Frame = 0;
                    s.State = 2;
                }
                s.X1 = s.X;
                s.Y1 = s.Y;
            }
        }

        private void EraseLasers()
        {
            foreach (var s in Shots)
            {
                if (s.State != 0 && Game.ShotTypes.IsHitscan(s.Type))
                {
                    TraceLine(s, destroy: true);
                    s.State = 0;
                }
            }
        }

        /// <summary>Destroys shoot-away cells in a (2r+2)² square and damages what is inside.</summary>
        public void AreaBlast(int cx, int cy, int radius, int damage, bool hitEnemies, bool hitPlayer)
        {
            int r = radius + 1;
            int left = Math.Max(0, cx - r), top = Math.Max(0, cy - r);
            var rect = new Engine.Rect(left, top, Math.Min(MapW, cx + r) - left, Math.Min(MapH, cy + r) - top);
            for (int x = cx - r; x < cx + r; x++)
            {
                for (int y = cy - r; y < cy + r; y++)
                {
                    if (x > 0 && x < MapW && y > 0 && y < MapH)
                    {
                        int idx = y * MapW + x;
                        if (Map.Tile(idx) != 0 && (Map.B2(idx) & 0x80) != 0)
                        {
                            Map.SetTile(idx, 0);
                            Map.SetB2(idx, (byte)(Map.B2(idx) & 0x7f));
                            MapPlot(x, y, 1, 1, 0);
                        }
                    }
                }
            }
            var p = Player;
            if (hitPlayer && p.Form != PlayerState.Tank && !rect.Intersect(new Engine.Rect(p.X, p.Y, FormW[p.Form], FormH[p.Form])).IsEmpty)
            {
                DamagePlayer(damage, false);
            }
            if (hitEnemies)
            {
                for (int i = 1; i < Enemies.Length; i++)
                {
                    var t = Enemies[i];
                    if (!rect.Intersect(new Engine.Rect(t.X, t.Y, t.W, t.H)).IsEmpty)
                    {
                        EnemyDamage(i, damage);
                    }
                }
            }
        }

        // ---- Bombs (timed objects) and inventory timers --------------------------------------------------

        /// <summary>A bomb on its fuse; its position is a level object (kind 2) so it falls.</summary>
        public void SpawnBomb(int x, int y, int type)
        {
            if (type == 0)
            {
                return;
            }
            TimedObject? t = null;
            foreach (var candidate in Timed)
            {
                if (candidate.Active == 0)
                {
                    t = candidate;
                    break;
                }
            }
            if (t == null)
            {
                return;
            }
            var item = Items[type];
            t.Active = 1;
            t.Type = type;
            t.NetOwner = 0;
            t.ExplLeft = 0;
            t.Triggered = 0;
            t.Fuse = item.Timer;
            t.Variant = item.Explosions.Length == 0 ? 0 : Random.Next() % item.Explosions.Length;
            int slot = FreeLevelObject();
            if (slot < 0)
            {
                t.Active = 0;
                return;
            }
            Objects[slot].Kind = LevelObject.KindBomb;
            Objects[slot].X = x;
            Objects[slot].Y = y;
            t.ObjSlot = slot;
        }

        private void InventoryTimersAndBombs()
        {
            for (int i = 0; i < Inventory.Size; i++)
            {
                if (Inv.Owned[i] != 0 && Inv.Active[i] != 0 && FrameCounter % 10 == 0)
                {
                    if (--Inv.Active[i] == 0)
                    {
                        if (--Inv.Count[i] <= 0)
                        {
                            Inv.Count[i] = 0;
                            Inv.Owned[i] = 0;
                        }
                        ItemEffect(i, on: false);
                    }
                }
            }

            foreach (var t in Timed)
            {
                if (t.Active == 0)
                {
                    continue;
                }
                var o = Objects[t.ObjSlot];
                var item = Items[t.Type];
                int explLeft = t.ExplLeft;
                SceneTag(EntityObject | t.ObjSlot); // The bomb falls with its object slot
                DrawBomb(t.Type, o.X, o.Y, t.Fuse, ref explLeft, t.Variant);
                SceneTag(0);
                t.ExplLeft = explLeft;
                if (t.Fuse == 0)
                {
                    if (t.ExplLeft == 0)
                    {
                        t.Active = 0;
                        o.Kind = 0;
                    }
                }
                else if (FrameCounter % 6 == 0 || t.Triggered != 0)
                {
                    if (--t.Fuse == 0)
                    {
                        if (item.Explosions.Length == 0)
                        {
                            t.Active = 0;
                            o.Kind = 0;
                        }
                        else
                        {
                            var e = item.Explosions[Math.Min(t.Variant, item.Explosions.Length - 1)];
                            t.ExplLeft = e.Frames;
                            if (item.Damage != 0)
                            {
                                if (ShakeRequest == 0)
                                {
                                    ShakeRequest = 0xd1;
                                }
                                if (e.Sound != 0xffff)
                                {
                                    Sounds.Play(e.Sound);
                                }
                                AreaBlast(o.X, o.Y, item.BlastRadius, item.Damage, hitEnemies: t.Type != 0x22, hitPlayer: true);
                            }
                        }
                    }
                }
            }
        }

        private void DrawBomb(int type, int x, int y, int fuse, ref int explLeft, int variant)
        {
            var item = Items[type];
            if (fuse != 0)
            {
                if (item.BombSprite == null)
                {
                    DrawSpriteCells(Art.Objects, x, y, 2, 2, type, Art.Tints.Identity);
                }
                else
                {
                    DrawSpriteCells(Art.Sprite(item.BombSprite), x, y, item.BombW, item.BombH, 0, Art.Tints.Identity);
                }
                return;
            }
            if (explLeft == 0 || item.Explosions.Length == 0)
            {
                return;
            }
            var e = item.Explosions[Math.Min(variant, item.Explosions.Length - 1)];
            int ex = Math.Max(0, x - e.XOff), ey = Math.Max(0, y - e.YOff);
            if (type == 6 || type == 7)
            {
                ey = y + item.BombH - e.H; // The explosion sits on the bomb's bottom
            }
            DrawSpriteCells(Art.Sprite(e.Sprite), ex, ey, e.W, e.H, e.Frames - explLeft, Art.Tints.Identity);
            explLeft--;
        }
    }
}
