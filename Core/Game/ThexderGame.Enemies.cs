namespace Thexder.Core.Game
{
    // Enemies: the type table, level setup, the per-tick think and draw passes, damage, death and spawning.
    internal sealed partial class ThexderGame
    {
        public const int EnemyCount = 100;
        public const int EnemyTypeSlots = 64;

        // Taunts spoken (10 %) when an enemy worth more than 4 hit points dies.
        private static readonly int[] EnemyKillTaunts = { 0x28, 0x9a, 0x4e, 0x9b, 0x22, 0x9c, 0x1c, 0x2a, 0x27, 0x9d, 0x9e, 0x4d, 0x9f, 0xa0, 0x29 };

        private readonly int[] enemyNewX = new int[EnemyCount];
        private readonly int[] enemyNewY = new int[EnemyCount];
        private readonly int[] enemyNewDir = new int[EnemyCount];

        private readonly bool[] enemyTypeUsed = new bool[EnemyTypeSlots];
        private readonly bool[] explosionUsed = new bool[DeathExplosions.Count];

        public EnemyType?[] EnemyTypes { get; } = new EnemyType?[EnemyTypeSlots];

        /// <summary>The type of an enemy record; null for platforms (type 0x6E) and ids whose slot is not loaded.</summary>
        public EnemyType? TypeOf(EnemyRecord t) => t.Type < EnemyTypeTable.Count ? EnemyTypes[t.Type] : null;

        /// <summary>Death explosions the current level's types use.</summary>
        public bool ExplosionUsed(int id) => explosionUsed[id];

        /// <summary>Rebuilds the loaded type slots for the current cell size (shot origins depend on it).</summary>
        public void ReloadEnemyTypes()
        {
            for (int id = 0; id < EnemyTypeTable.Count; id++)
            {
                if (EnemyTypes[id] == null)
                {
                    continue;
                }
                if (id == EnemyTypeTable.FlyingSphinx)
                {
                    EnemyTypes[id] = EnemyTypes[EnemyTypeTable.SphinxStatue] != null
                        ? EnemyTypeTable.BuildFlyingSphinx(S)
                        : EnemyTypeTable.Placeholder(id);
                }
                else
                {
                    EnemyTypes[id] = EnemyTypeTable.Build(id, S);
                }
            }
        }

        /// <summary>Builds or frees one slot.</summary>
        private void EnemyTypeLoad(int id, bool load)
        {
            if (id < 0 || id >= EnemyTypeTable.Count)
            {
                return;
            }
            if (!load)
            {
                if (EnemyTypes[id] != null && id != EnemyTypeTable.FlyingSphinx)
                {
                    EnemyTypes[id] = null;
                    if (id == EnemyTypeTable.SphinxStatue)
                    {
                        EnemyTypes[EnemyTypeTable.FlyingSphinx] = null;
                    }
                }
                return;
            }
            if (EnemyTypes[id] != null)
            {
                return;
            }
            EnemyTypes[id] = EnemyTypeTable.Build(id, S);
            if (id == EnemyTypeTable.SphinxStatue)
            {
                EnemyTypes[EnemyTypeTable.FlyingSphinx] = EnemyTypeTable.BuildFlyingSphinx(S);
            }
        }

        /// <summary>Loads the types the level uses and resets the runtime fields.</summary>
        private void EnemyLevelSetup(bool fresh)
        {
            Array.Clear(enemyTypeUsed);
            Array.Clear(explosionUsed);
            for (int i = 1; i < EnemyCount; i++)
            {
                var r = Enemies[i];
                if (r.State == EnemyRecord.Alive)
                {
                    if (r.Type < EnemyTypeTable.Count)
                    {
                        enemyTypeUsed[r.Type] = true;
                    }
                    else
                    {
                        r.Type = 0;
                        enemyTypeUsed[0] = true;
                    }
                }
                else if (!fresh && (r.State == EnemyRecord.Dying || r.State == EnemyRecord.Remains) && r.Type < EnemyTypeSlots)
                {
                    enemyTypeUsed[r.Type] = true;
                }
                if (World == 10 && Level == 5)
                {
                    if (r.Type == 0x29)
                    {
                        enemyTypeUsed[0x29] = true;
                        PrimusFlyerSlot = i;
                    }
                    if (r.Type == 0x2a)
                    {
                        enemyTypeUsed[0x2a] = true;
                        PrimusWalkerSlot = i;
                    }
                }
            }

            MarkReserveTypes();
            EnemyTypeLoad(0x0c, false);
            EnemyTypeLoad(0x19, false);
            for (int t = 0; t < EnemyTypeSlots; t++)
            {
                EnemyTypeLoad(t, enemyTypeUsed[t]);
            }
            if (enemyTypeUsed[0x1b])
            {
                EnemyTypeLoad(0x0c, true); // Bug Breath throws Bug Bombs
                enemyTypeUsed[0x0c] = true;
            }
            if (enemyTypeUsed[0x23])
            {
                EnemyTypeLoad(0x19, true); // Mega Gun launches Mega Mines
                enemyTypeUsed[0x19] = true;
            }
            for (int t = 0; t < EnemyTypeTable.Count; t++)
            {
                var type = EnemyTypes[t];
                if (enemyTypeUsed[t] && type != null)
                {
                    for (int k = 0; k < type.ExplCount; k++)
                    {
                        if (type.Expl[k] >= 0)
                        {
                            explosionUsed[type.Expl[k]] = true;
                        }
                    }
                }
            }

            for (int i = 1; i < EnemyCount; i++)
            {
                var r = Enemies[i];
                EnemyPrepare(r, fresh);
                if (r.State != EnemyRecord.Free && r.State != EnemyRecord.Remains)
                {
                    EnemyOccupancy(i, mark: true, useCurrent: true);
                }
            }
        }

        /// <summary>
        /// EnemyLevelSetup's per-record part: sizes and flags from the type, runtime fields reset, the direction a mover
        /// needs.
        /// </summary>
        private void EnemyPrepare(EnemyRecord r, bool fresh)
        {
            if (fresh)
            {
                var type = TypeOf(r);
                if (r.State != EnemyRecord.Free && type != null)
                {
                    r.W = type.W;
                    r.H = type.H;
                    r.NFrames = type.Frames & 0xff;
                    r.NDieFrames = type.DieFrames & 0xff;
                    r.Flags = type.Flags;
                    r.Weapon = type.Weapon;
                }
                r.Attacking = 0;
                r.AtkLatch = 0;
                r.AtkIdx = 0;
                r.AnimTick = 0;
                r.Timer = 0;
                r.Aux = 0;
                r.Frame = 0;
                r.Animate = 1;
                r.AtkCooldown = 0;
                r.AtkCount = 0;
                r.FlinchAcc = 0;
            }
            switch (r.Mode)
            {
                case 7:
                    if (r.Dir != 1 && r.Dir != 2)
                    {
                        r.Dir = 1;
                    }
                    break;
                case 8:
                    if (r.Dir != 4 && r.Dir != 8)
                    {
                        r.Dir = 4;
                    }
                    break;
                case 9:
                    r.Dir = 5;
                    break;
                case 10:
                    r.Dir = 6;
                    break;
            }
        }

        /// <summary>
        /// Where enemy i stands for the rest of this frame - the AI's pending move (committed by EnemyAnimateDrawAll) when
        /// there is one, clamped to the map, else the record's position.
        /// </summary>
        private (int X, int Y) EnemyCell(int i)
        {
            var r = Enemies[i];
            if (enemyNewX[i] == 0 && enemyNewY[i] == 0)
            {
                return (r.X, r.Y);
            }
            return (Math.Min(enemyNewX[i], MapW - r.W), Math.Min(enemyNewY[i], MapH - r.H));
        }

        /// <summary>
        /// Writes or clears the enemy index in cell byte 2 over its box, and its red mark ('&amp;' = 0x26) on the Map of
        /// Planet while it is flagged as seen.
        /// </summary>
        private void EnemyOccupancy(int i, bool mark, bool useCurrent)
        {
            var r = Enemies[i];
            int x = r.X, y = r.Y;
            if (!useCurrent && (enemyNewX[i] != 0 || enemyNewY[i] != 0))
            {
                x = enemyNewX[i];
                y = enemyNewY[i];
            }
            byte[] radar = MapSurface.Pixels;
            for (int cy = y; cy < y + r.H; cy++)
            {
                for (int cx = x; cx < x + r.W; cx++)
                {
                    int c = cy * MapW + cx;
                    if ((uint)c >= Map.Count)
                    {
                        continue;
                    }
                    if (!mark)
                    {
                        Map.SetB2(c, (byte)(Map.B2(c) & 0x80));
                        if (radar[c] == 0x26)
                        {
                            radar[c] = 0;
                        }
                        if (!ViewObjectsActive)
                        {
                            r.Flags &= ~EnemyRecord.FlagVisible;
                        }
                    }
                    else
                    {
                        Map.SetB2(c, (byte)((Map.B2(c) & 0x80) | (i & 0x7f)));
                        if (useCurrent && (r.Flags & EnemyRecord.FlagVisible) != 0 && radar[c] == 0)
                        {
                            radar[c] = 0x26;
                        }
                    }
                }
            }
        }

        private int EnemyPlayerCenterX => (ushort)((FormW[Player.Form] >> 1) + Player.X);

        private int EnemyPlayerCenterY => (ushort)((FormH[Player.Form] >> 1) + Player.Y);

        private void EnemyPlaySound(int id, int x, int y) => Sounds.PlayPositional(id, x, y, Player.X, Player.Y);

        private bool EnemySameBitmap(string? a, string? b) => Art.Sprite(a) == null && Art.Sprite(b) == null;

        private bool EnemyHasBitmap(string? name) => Art.Sprite(name) != null;

        private void EnemyDraw(string? sprite, int x, int y, int w, int h, int frame, byte[] lut) =>
            DrawSpriteCells(Art.Sprite(sprite), (ushort)x, (ushort)y, (ushort)w, (ushort)h, (ushort)frame, lut);

        /// <summary>Activation, AI movement and firing of every enemy.</summary>
        private void EnemyThinkAll()
        {
            Array.Clear(enemyNewX);
            Array.Clear(enemyNewY);
            Array.Clear(enemyNewDir);
            for (int i = 1; i < EnemyCount; i++)
            {
                var r = Enemies[i];
                if (r.State == EnemyRecord.Free)
                {
                    continue;
                }
                int x = r.X, y = r.Y, h = r.H, w = r.W, rngY = r.Range;
                int rngX = ViewWidthCells == 60 ? (rngY + 10) & 0xffff : rngY;
                int frame = r.Frame;
                bool busy = (r.Flags & EnemyRecord.BusyMask) != 0;
                if (r.State != EnemyRecord.Remains)
                {
                    EnemyOccupancy(i, mark: false, useCurrent: true);
                }
                int dir = r.Dir;
                int nx = x, ny = y;
                int tcx = (ushort)((w >> 1) + x);
                int q = h >> 1;
                if (q <= h - 3)
                {
                    q = h - 3;
                }
                if (LevelGfxReady)
                {
                    if (Math.Abs(tcx - EnemyPlayerCenterX) > rngX || Math.Abs((ushort)(q + y) - EnemyPlayerCenterY) > rngY)
                    {
                        if (r.AtkLatch != 0)
                        {
                            r.AtkLatch = 0;
                        }
                    }
                    else if (!busy)
                    {
                        EnemyAI(r, ref nx, ref ny, ref dir, ref frame);
                        enemyNewX[i] = nx & 0xffff;
                        enemyNewY[i] = ny & 0xff;
                        enemyNewDir[i] = (dir & 0xff) | (r.State == EnemyRecord.Dying ? 0x20 : r.State == EnemyRecord.Remains ? 0x40 : 0);
                    }
                }
                var type = TypeOf(r);
                if (r.State == EnemyRecord.Alive && OnScreen(nx, ny, w, h) && r.Weapon >= 0 && type != null)
                {
                    EnemyFire(i, r, type);
                }
                if (r.State != EnemyRecord.Remains && r.State != EnemyRecord.Dying)
                {
                    EnemyOccupancy(i, mark: true, useCurrent: false);
                }
            }
        }

        /// <summary>
        /// Commits the AI positions, starts turns and attacks, draws every enemy (alive, dying with its explosions and
        /// points, remains), advances animations and picks the boss for the HUD.
        /// </summary>
        private void EnemyAnimateDrawAll()
        {
            byte[] radar = MapSurface.Pixels;
            for (int c = 0; c < Map.Count; c++)
            {
                int b = Map.B2(c) & 0x7f;
                if (b != 0 && b < 0x75)
                {
                    Map.SetB2(c, (byte)(Map.B2(c) & 0x80));
                    radar[c] = 0;
                }
            }
            for (int i = 1; i < EnemyCount; i++)
            {
                var r = Enemies[i];
                if (r.State == EnemyRecord.Free)
                {
                    r.Flags &= ~EnemyRecord.FlagHitFlash;
                    continue;
                }
                var type = TypeOf(r);
                if (type == null)
                {
                    continue;
                }
                EnemyAnimateDraw(i, r, type);
            }
            SceneTag(0);
        }

        private void EnemyAnimateDraw(int i, EnemyRecord r, EnemyType type)
        {
            SceneTag(EntityEnemy | i);
            int x = r.X, y = r.Y, w = r.W, h = r.H, frame = r.Frame;
            int rngY = r.Range;
            int rngX = ViewWidthCells == 60 ? (rngY + 10) & 0xffff : rngY;
            byte[] lut = (r.Flags & EnemyRecord.FlagHitFlash) != 0 ? Art.Tints.Hit : Art.Tints.Identity;
            bool busy = (r.Flags & EnemyRecord.BusyMask) != 0;
            int dir = r.Dir;
            int ox = x, oy = y;
            if (LevelGfxReady && (enemyNewX[i] != 0 || enemyNewY[i] != 0))
            {
                x = enemyNewX[i];
                y = enemyNewY[i];
                dir = enemyNewDir[i];
                if (MapW - r.W < x)
                {
                    x = (MapW - r.W) & 0xffff;
                }
                if (MapH - r.H < y)
                {
                    y = (MapH - r.H) & 0xffff;
                }
            }
            if (NetActive && LevelGfxReady)
            {
                NetEnemyUpdate(i, r, enemyNewX[i], enemyNewY[i], ref dir);
            }
            r.X = x;
            r.Y = y;

            if (r.State == EnemyRecord.Alive && r.AtkLatch == 0 && !busy
                && (((r.Dir & 1) != 0 && (dir & 2) != 0) || ((r.Dir & 2) != 0 && (dir & 1) != 0)) && type.TurnFrames != 0)
            {
                frame = (dir & 2) == 0 ? (type.TurnFrames * 2 - 1) & 0xffff : 0;
                r.Flags |= EnemyRecord.FlagTurning;
            }
            bool chain = false;
            if (r.State == EnemyRecord.Alive && r.Attacking != 0 && r.AtkLatch == 0)
            {
                chain = EnemyAttackDecide(r, type);
            }
            r.Dir = dir;

            int tcx = (ushort)((w >> 1) + x);
            int q = h >> 1;
            if (q <= h - 3)
            {
                q = h - 3;
            }
            int tcy = (ushort)(q + y);
            int pcx = EnemyPlayerCenterX, pcy = EnemyPlayerCenterY;
            if (LevelGfxReady && Math.Abs(tcx - pcx) <= rngX && Math.Abs(tcy - pcy) <= rngY)
            {
                EnemySpecialTouch(r, i);
            }
            if (OnScreen(x, y, w, h))
            {
                r.Flags |= EnemyRecord.FlagVisible;
                if (type.IsBoss && BossShown != i && r.State == EnemyRecord.Alive)
                {
                    HudFullRedraw = true;
                    BossShown = i;
                }
            }
            if (r.State != EnemyRecord.Remains && r.State != EnemyRecord.Dying)
            {
                EnemyOccupancy(i, mark: true, useCurrent: true);
            }

            int advance = frame; // The frame the animation step continues from
            if (r.State == EnemyRecord.Alive)
            {
                advance = EnemyDrawAlive(r, type, x, y, w, h, ox, oy, frame, dir, chain, tcx, tcy, pcx, pcy, lut);
            }
            else if (r.State == EnemyRecord.Dying)
            {
                advance = EnemyDrawDying(r, type, x, y, frame, lut);
            }
            else if (r.State == EnemyRecord.Remains)
            {
                EnemyDraw(type.SprDieL, x, y, w, h, type.RemFrame, lut);
            }
            r.Flags &= ~(EnemyRecord.FlagFlinch | EnemyRecord.FlagHitFlash);
            if (r.State != EnemyRecord.Dying && r.State != EnemyRecord.Remains && r.Animate != 0)
            {
                EnemyAdvanceAnimation(r, type, advance);
            }
        }

        // Returns the frame to advance from.
        private int EnemyDrawAlive(EnemyRecord r, EnemyType type, int x, int y, int w, int h, int ox, int oy, int frame, int dir,
            bool chain, int tcx, int tcy, int pcx, int pcy, byte[] lut)
        {
            int dX = x, dY = y, dW = r.W, dH = r.H;
            string? sprite = null;
            bool spriteIsL = false;
            int fl = r.Flags;
            if ((fl & EnemyRecord.FlagSphinxTransition) != 0)
            {
                var flying = EnemyTypes[EnemyTypeTable.FlyingSphinx];
                sprite = (fl & EnemyRecord.FlagFacingLeft) != 0 ? flying?.SprDieL : flying?.SprDieR;
            }
            else if ((fl & EnemyRecord.FlagTurning) != 0)
            {
                sprite = type.SprTurn;
                bool done = false;
                if ((dir & 1) == 0)
                {
                    frame = (frame + 1) & 0xffff;
                    if (type.TurnFrames * 2 - 1 <= frame)
                    {
                        done = true;
                    }
                }
                else if (frame == 0 || (frame = (frame - 1) & 0xffff) == 0)
                {
                    done = true;
                }
                if (done)
                {
                    r.Flags = fl & ~EnemyRecord.FlagTurning;
                }
                r.Frame = frame;
                r.Animate = 0;
            }
            else
            {
                int latch = r.AtkLatch;
                if (latch == 0 && (fl & EnemyRecord.FlagFlinch) != 0 && EnemyHasBitmap(type.SprFlinchL))
                {
                    r.Flags = fl | EnemyRecord.FlagFacingLeft;
                    sprite = type.SprFlinchL;
                    if ((dir & 2) != 0)
                    {
                        sprite = type.SprFlinchR;
                        r.Flags = fl & ~EnemyRecord.FlagFacingLeft;
                    }
                    frame = 1;
                }
                else if ((fl & (EnemyRecord.FlagSinking | EnemyRecord.FlagRising)) == 0)
                {
                    bool inAttack = LevelGfxReady && type.AtkCount != 0
                        && Math.Abs(tcx - pcx) <= type.AtkRange && Math.Abs(tcy - pcy) <= type.AtkRange;
                    if (!inAttack)
                    {
                        if (r.Attacking != 0)
                        {
                            if (latch == 0)
                            {
                                r.Frame = 0;
                                r.NFrames = type.Frames & 0xff;
                                r.Attacking = 0;
                                r.AtkCooldown = 0;
                            }
                        }
                        else if (latch == 0 && r.NFrames <= frame)
                        {
                            r.Frame = 0;
                        }
                    }
                    else if (r.Attacking == 0)
                    {
                        if (EnemyAttackDecide(r, type))
                        {
                            r.Frame = 0;
                            r.NFrames = type.Attacks[r.AtkIdx].Frames & 0xff;
                            r.Attacking = 1;
                        }
                    }
                    else if (chain)
                    {
                        r.Frame = 0; // Next attack of a burst
                        r.NFrames = type.Attacks[r.AtkIdx].Frames & 0xff;
                    }
                    else if (latch == 0 && r.NFrames <= frame)
                    {
                        r.Frame = 0;
                    }

                    frame = r.Frame;
                    if (r.Attacking == 0)
                    {
                        r.Flags |= EnemyRecord.FlagFacingLeft;
                        sprite = type.SprL;
                        spriteIsL = true;
                        if ((dir & 2) != 0)
                        {
                            r.Flags &= ~EnemyRecord.FlagFacingLeft;
                            sprite = type.SprR;
                            spriteIsL = false;
                        }
                        if (type.HasStand && x == ox && y == oy)
                        {
                            frame = (dir & 2) != 0 ? (type.Frames - type.StandFrame - 1) & 0xffff : type.StandFrame;
                        }
                        if (LevelGfxReady && type.IdleSnd != -1 && OnScreen(x, y, w, h))
                        {
                            if (type.IdleSndMode == 1)
                            {
                                if (Random.Next() < type.IdleSndArg * 0x147)
                                {
                                    EnemyPlaySound(type.IdleSnd, r.X, r.Y);
                                }
                            }
                            else if (type.IdleSndMode == 2 && r.AtkIdx == 0 && type.IdleSndArg == frame)
                            {
                                EnemyPlaySound(type.IdleSnd, r.X, r.Y);
                            }
                        }
                    }
                    else
                    {
                        var attack = type.Attacks[r.AtkIdx];
                        sprite = attack.SprL;
                        if (r.AtkLatch == 0)
                        {
                            r.Flags |= EnemyRecord.FlagFacingLeft;
                        }
                        if ((dir & 2) != 0)
                        {
                            sprite = attack.SprR;
                            if (r.AtkLatch == 0)
                            {
                                r.Flags &= ~EnemyRecord.FlagFacingLeft;
                            }
                        }
                        if (dH != attack.H)
                        {
                            // Bottom aligned; the width is only taken when the height differs
                            dW = attack.W;
                            dY = (dY - attack.H + dH) & 0xffff;
                            dH = attack.H;
                        }
                        if (attack.Snd != -1)
                        {
                            if (attack.SndMode == 1 && Random.Next() < attack.SndArg * 0x147)
                            {
                                EnemyPlaySound(attack.Snd, r.X, r.Y);
                            }
                            else if (attack.SndMode == 2 && r.AtkIdx == 0 && r.AnimTick == 0 && attack.SndArg == r.Frame)
                            {
                                if (r.Type == 8)
                                {
                                    Sounds.Play(attack.Snd);
                                }
                                else
                                {
                                    EnemyPlaySound(attack.Snd, r.X, r.Y);
                                }
                            }
                        }
                    }
                }
                else
                {
                    sprite = type.Attacks[1].SprL; // Necromorgue sinking / rising (NecroMorgueMove2)
                    r.Animate = 1;
                }
            }

            int advance = frame;
            if (r.Type == 0x0c && r.Mode == 0x13)
            {
                // Bug Bomb being thrown out of a Bug Breath
                EnemyDraw(type.Attacks[0].SprL, dX, dY, dW, dH, frame, lut);
                if (advance == 5)
                {
                    r.Mode = type.Mode;
                    r.Speed = type.Speed;
                    advance = type.Frames;
                }
            }
            else if (r.Type == 0x1b)
            {
                // Bug Breath: 5-cell head (attack / flinch strip) and a still body
                int headFrame = frame;
                string? head = sprite;
                if (spriteIsL)
                {
                    headFrame = 0;
                    head = type.SprFlinchL;
                }
                EnemyDraw(head, dX, dY, 5, dH, headFrame, lut);
                EnemyDraw(type.SprL, dX + 5, dY, dW - 5, dH, 0, lut);
            }
            else
            {
                EnemyDraw(sprite, dX, dY, dW, dH, frame, lut);
                if (r.Type == 0x1c && (r.Flags & EnemyRecord.FlagFlinch) != 0)
                {
                    // Necromorgue: a flinch makes it sink into the ground
                    if ((r.Flags & EnemyRecord.FlagTurning) == 0)
                    {
                        r.Frame = 0;
                        r.Animate = 0;
                    }
                    r.Flags |= EnemyRecord.FlagSinking;
                }
            }
            return advance;
        }

        // Returns the frame to advance from.
        private int EnemyDrawDying(EnemyRecord r, EnemyType type, int x, int y, int frame, byte[] lut)
        {
            int fl = r.Flags;
            if ((fl & EnemyRecord.FlagSphinxTransition) != 0)
            {
                var flying = EnemyTypes[EnemyTypeTable.FlyingSphinx];
                int f;
                string? sprite;
                if ((fl & EnemyRecord.FlagFacingLeft) == 0)
                {
                    f = (2 - r.Frame) & 0xffff;
                    sprite = flying?.SprDieR;
                }
                else
                {
                    f = r.Frame;
                    sprite = flying?.SprDieL;
                }
                EnemyDraw(sprite, x, y, r.W, r.H, f, lut);
                r.Frame = (r.Frame + 1) & 0xffff;
                if (r.Frame > 2)
                {
                    r.Flags &= ~EnemyRecord.FlagSphinxTransition;
                    r.Type = EnemyTypeTable.SphinxStatue;
                    r.Frame = 0;
                }
                return f;
            }
            if ((fl & EnemyRecord.FlagTurning) != 0)
            {
                int f = (fl & EnemyRecord.FlagFacingLeft) == 0 ? (type.TurnFrames * 2 - r.Frame - 1) & 0xffff : r.Frame;
                EnemyDraw(type.SprTurn, x, y, r.W, r.H, f, lut);
                r.Frame = (r.Frame + 1) & 0xffff;
                if (type.TurnFrames <= r.Frame)
                {
                    r.Flags &= ~EnemyRecord.FlagTurning;
                    r.Frame = 0;
                }
                return f;
            }

            int need = 0;
            int dx = x - type.DieDX;
            if (dx < 1)
            {
                dx = 0;
            }
            int dy = y - type.DieDY;
            if (dy < 1)
            {
                dy = 0;
            }
            if (r.Frame < type.DieFrames)
            {
                string? sprite = (fl & EnemyRecord.FlagFacingLeft) == 0 ? type.SprDieR : type.SprDieL;
                // The Primus Crawler and Flyer play their strip backwards (folding into the next form)
                int f = r.Type == 0x1e || r.Type == 0x29 ? type.DieFrames - r.Frame - 1 : r.Frame;
                EnemyDraw(sprite, dx, dy, type.DieW, type.DieH, f, lut);
                if (r.Type == 0x2e && type.DieFrames - r.Frame == 1)
                {
                    // Barrel Bots blow up on their last death frame
                    AreaBlast((ushort)((r.W >> 1) + r.X), (ushort)((r.H >> 1) + r.Y), (short)type.BlastRad, type.BlastDmg, true, true);
                }
            }
            else if (type.RemFrame != 0)
            {
                int rx = x + type.RemDX;
                if (rx < 1)
                {
                    rx = 0;
                }
                int ry = y + type.RemDY;
                if (ry < 1)
                {
                    ry = 0;
                }
                EnemyDraw(type.SprDieL, rx, ry, type.RemW, type.RemH, type.RemFrame, lut);
            }
            if (type.HasExpl)
            {
                need = EnemyDrawDeathExplosions(r, type, dx, dy);
            }

            int sx = (type.DieW >> 1) + dx;
            if (sx > MapW - 1)
            {
                sx = MapW;
            }
            int sy = dy - 1;
            if (sy < 1)
            {
                sy = 0;
            }
            short popupX = (short)((sx - CamX - 1) * S);
            short popupY = (short)((sy - CamY) * S);
            int points = r.Hp * Header.PointsPerHit;
            if (!NetActive && popupX >= 0 && popupY >= 0) // Network play: no score for enemies
            {
                DrawPopupNumber(points, popupX, popupY, 5, FrameCounter % 2 != 0, false);
            }
            if (r.Frame == 0 && !NetActive)
            {
                Player.Score += (uint)points;
                if (r.Hp > 4 && type.Mode != 0 && Random.Next() % 10 == 5)
                {
                    Sounds.Play(EnemyKillTaunts[Random.Next() % 15]);
                }
            }

            r.Frame = (r.Frame + 1) & 0xffff;
            if (r.NDieFrames <= r.Frame && need <= r.Frame)
            {
                if (type.IsBoss)
                {
                    Sounds.Play(0xd2);
                }
                if (type.RemFrame == 0)
                {
                    r.State = EnemyRecord.Free;
                    if (r.Type == 0x1e)
                    {
                        if (PrimusFlyerSlot != 0)
                        {
                            Enemies[PrimusFlyerSlot].State = EnemyRecord.Alive;
                            Sounds.Play(0x8d);
                        }
                    }
                    else if (r.Type == 0x29 && PrimusWalkerSlot != 0)
                    {
                        Enemies[PrimusWalkerSlot].State = EnemyRecord.Alive;
                        Sounds.Play(0x8d);
                    }
                }
                else
                {
                    frame = 0;
                    r.State = EnemyRecord.Remains;
                    r.Frame = 0;
                    r.NFrames = type.RemFrames & 0xff;
                    r.X = (x + type.RemDX) & 0xffff;
                    r.Y = (y + type.RemDY) & 0xffff;
                    r.H = type.RemH;
                    r.W = type.RemW;
                    if (r.Type == 0x2a)
                    {
                        EnemyPrimusWalkerDefeated();
                    }
                }
            }
            return frame;
        }

        private void EnemyPrimusWalkerDefeated()
        {
            if (Player.Form == PlayerState.Walker)
            {
                StartTurn();
            }
            else
            {
                if (Player.Y > 0x3b)
                {
                    Player.Y = 0x3b;
                }
                Player.Jumping = 0;
                Player.StateFlags |= PlayerState.FlagTransforming;
                Player.Frame = 0;
                Player.TransformTarget = 0;
            }
            Player.StateFlags |= PlayerState.FlagGoingHome;
        }

        /// <summary>Draws this frame of the death explosions; returns the frames they need.</summary>
        private int EnemyDrawDeathExplosions(EnemyRecord r, EnemyType type, int dx, int dy)
        {
            bool multi = type.IsBoss && type.ExplCount > 1;
            if (r.Frame == 0 && !multi)
            {
                r.Aux = type.ExplCount == 1 ? 0 : Random.Next() & 1; // One of the two variants
            }
            if (!multi)
            {
                int e = type.Expl[r.Aux & 3];
                if (e == -1)
                {
                    return 0;
                }
                var ex = DeathExplosions.Table[e];
                int x = (short)((type.DieW >> 1) + dx) - ex.CX;
                int y = (short)((type.DieH >> 1) + dy) - ex.CY;
                EnemyDrawDeathExplosion(e, r.Frame, type.ExplDelay0, x < 1 ? 0 : x, y < 1 ? 0 : y);
                return (ex.Frames + type.ExplDelay0) & 0xffff;
            }
            int need = 0;
            for (int k = 0; k < type.ExplCount; k++)
            {
                int e = type.Expl[k];
                var ex = DeathExplosions.Table[e];
                if (need < ex.Frames + type.ExplDelay[k])
                {
                    need = (ex.Frames + type.ExplDelay[k]) & 0xffff;
                }
                int x = (short)(type.ExplDX[k] + dx) - ex.CX;
                int y = (short)(type.ExplDY[k] + dy) - ex.CY;
                EnemyDrawDeathExplosion(e, r.Frame, type.ExplDelay[k], x < 1 ? 0 : x, y < 1 ? 0 : y);
            }
            return need;
        }

        /// <summary>Frame (timer - delay) of explosion e, with its sound on the first frame.</summary>
        private void EnemyDrawDeathExplosion(int e, int timer, int delay, int x, int y)
        {
            var ex = DeathExplosions.Table[e];
            int f = (ushort)(timer - delay);
            if (delay <= timer && (short)f < ex.Frames)
            {
                EnemyDraw(ex.Sprite, x, y, ex.W, ex.H, f, Art.Tints.Identity);
                if (f == 0 && ex.Sound != 0xffff)
                {
                    Sounds.Play(ex.Sound);
                }
            }
        }

        private void EnemyAdvanceAnimation(EnemyRecord r, EnemyType type, int frame)
        {
            int fl = r.Flags;
            if ((fl & EnemyRecord.FlagSphinxTransition) != 0)
            {
                bool done;
                if (r.Type == EnemyTypeTable.FlyingSphinx)
                {
                    frame = (frame - 1) & 0xffff;
                    done = frame == 0;
                }
                else
                {
                    frame = (frame + 1) & 0xffff;
                    done = frame > 2;
                }
                if (done)
                {
                    r.Flags = fl & 0xfcdf;
                    if (r.Type == EnemyTypeTable.FlyingSphinx)
                    {
                        r.Type = EnemyTypeTable.SphinxStatue;
                    }
                    else
                    {
                        r.Type = EnemyTypeTable.FlyingSphinx;
                        r.Dir = (r.Flags & EnemyRecord.FlagFacingLeft) == 0 ? 2 : 1;
                    }
                    var next = TypeOf(r);
                    if (next != null)
                    {
                        r.Mode = next.Mode;
                    }
                    frame = 0;
                }
            }
            else if ((fl & EnemyRecord.FlagSinking) == 0 && (fl & EnemyRecord.FlagRising) == 0)
            {
                r.AnimTick = (r.AnimTick + 1) & 0xffff;
                int delay = TypeOf(r)?.AnimDelay ?? 1;
                if (delay == 0 || r.AnimTick % delay != 0)
                {
                    return; // R.frame is not written this tick
                }
                frame = (frame + 1) & 0xffff;
                r.AnimTick = 0;
                if (r.NFrames <= frame)
                {
                    frame = 0;
                }
            }
            else if ((fl & EnemyRecord.FlagSinking) == 0)
            {
                if (frame == 0)
                {
                    r.Flags = fl & 0xfd5f; // Risen: clears rising, flinch and hit flash
                }
                else
                {
                    frame--;
                }
            }
            else
            {
                frame = (frame + 1) & 0xffff;
                if (type.Attacks[1].Frames <= frame)
                {
                    EnemyTeleportNextToPlayer(r);
                    frame = (type.Attacks[1].Frames - 1) & 0xffff;
                    r.Flags = (r.Flags & ~EnemyRecord.FlagSinking) | EnemyRecord.FlagRising;
                }
            }
            r.Frame = frame;
        }

        /// <summary>Starts or chains an attack; when the burst is over, reloads the cooldown.</summary>
        private bool EnemyAttackDecide(EnemyRecord r, EnemyType type)
        {
            int roll = Random.Next();
            if (r.Type == 0x2a && (r.Flags & EnemyRecord.FlagHopping) != 0)
            {
                return false; // Primus Walker in mid leap
            }
            if (type.AtkCount < 2)
            {
                if (type.Attacks[0].Chance == 100)
                {
                    r.AtkIdx = 0;
                    return true;
                }
                if (r.AtkCooldown == 0 && r.AtkCount < 3 && roll < type.Attacks[0].Chance * 0x147)
                {
                    r.AtkIdx = 0;
                    r.AtkCount = (r.AtkCount + 1) & 0xff;
                    return true;
                }
            }
            else if (r.AtkCooldown == 0 && r.AtkCount < 3 && roll < type.Attacks[0].Chance * 0x147)
            {
                r.AtkIdx = roll < type.Attacks[1].Chance * 0x147 ? 1 : 0;
                r.AtkCount = (r.AtkCount + 1) & 0xff;
                return true;
            }
            if (r.AtkCooldown == 0)
            {
                r.Frame = 0;
                r.NFrames = type.Frames & 0xff;
                r.Attacking = 0;
                r.AtkLatch = 0;
                r.AtkCount = 0;
                r.AtkCooldown = type.Frames & 0xff;
            }
            else if (r.AnimTick == 0)
            {
                r.AtkCooldown--;
            }
            return false;
        }

        /// <summary>Hit an enemy; kills it (death animation, blast, boss count) when spent.</summary>
        public void EnemyDamage(int index, int damage)
        {
            var r = Enemies[index];
            if (r.State != EnemyRecord.Alive)
            {
                return;
            }
            var type = TypeOf(r);
            if (type == null)
            {
                return;
            }
            r.Damage = (r.Damage + damage) & 0xffff;
            r.Flags |= EnemyRecord.FlagHitFlash;
            r.FlinchAcc = (r.FlinchAcc + damage) & 0xff;
            if (type.FlinchThr <= r.FlinchAcc)
            {
                r.Flags |= EnemyRecord.FlagFlinch;
                r.FlinchAcc = 0;
            }
            if (r.Type == 0x2a)
            {
                r.Aux = (r.Aux + 1) & 0xffff; // The Primus Walker leaps every third hit
            }
            if (r.Damage < r.Hp)
            {
                EnemyPlaySound(type.HitSnd, r.X, r.Y);
                return;
            }
            NetEnemyKilled(index);

            if (!EnemyHasBitmap(type.SprDieL) && !type.HasExpl)
            {
                if (type.RemFrame == 0)
                {
                    r.State = EnemyRecord.Free;
                }
                else
                {
                    r.State = EnemyRecord.Remains;
                    r.Frame = 0;
                    r.NFrames = type.RemFrames & 0xff;
                    r.X = (r.X + type.RemDX) & 0xffff;
                    r.Y = (r.Y + type.RemDY) & 0xffff;
                    r.H = type.RemH;
                    r.W = type.RemW;
                }
            }
            else
            {
                r.State = EnemyRecord.Dying;
                r.Mode = type.DieMode;
                r.Range = 0x280;
                r.Frame = 0;
                // "turn to the front first" needs DieL == DieR as pointers, which only two missing bitmaps are
                if (type.TurnFrames == 0 || !EnemySameBitmap(type.SprDieR, type.SprDieL))
                {
                    if (r.Type == EnemyTypeTable.FlyingSphinx)
                    {
                        r.Flags |= EnemyRecord.FlagTurning | EnemyRecord.FlagSphinxTransition;
                        r.Frame = 0;
                    }
                    else
                    {
                        r.Flags &= ~EnemyRecord.FlagTurning;
                    }
                }
                else
                {
                    r.Flags |= EnemyRecord.FlagTurning;
                    r.Frame = 0;
                    if (r.Type == EnemyTypeTable.FlyingSphinx)
                    {
                        r.Flags |= EnemyRecord.FlagSphinxTransition;
                    }
                }
            }
            EnemyPlaySound(type.DieSnd, r.X, r.Y);
            if (type.BlastDmg != 0 && r.Type != 0x2e)
            {
                AreaBlast((ushort)((r.W >> 1) + r.X), (ushort)((r.H >> 1) + r.Y), (short)type.BlastRad, type.BlastDmg, true, true);
            }
            if (type.IsBoss)
            {
                Header.BossCount = (Header.BossCount - 1) & 0xffff;
                if (Header.BossCount == 0)
                {
                    Header.ExitOpen = 1;
                }
            }
        }

        /// <summary>Per-type shot immunities (rubble never hurts enemies).</summary>
        public bool EnemyCanBeHitBy(int index, int shotType)
        {
            if (shotType > 0x14 && shotType < 0x1b)
            {
                return false;
            }
            var r = Enemies[index];
            switch (r.Type)
            {
                case 0x0f:
                case 0x1e:
                case 0x2a:
                    return shotType is not (1 or 8 or 2);
                case 0x13:
                case 0x26:
                    return shotType != 1;
                case 0x16:
                    return r.Attacking == 0 || r.AtkLatch == 0;
                case 0x1a:
                    return r.Attacking == 0;
                case 0x1c:
                    return (r.Flags & (EnemyRecord.FlagSinking | EnemyRecord.FlagRising)) == 0;
                case 0x24:
                    return false;
                case 0x29:
                    return shotType is not (1 or 2);
                default:
                    return true;
            }
        }

        /// <summary>
        /// Whether touching this enemy hurts the player this tick (mines never; most attackers only on one attack frame;
        /// everything else every ninth tick).
        /// </summary>
        public bool EnemyContactHurts(EnemyRecord t)
        {
            switch (t.Type)
            {
                case 0x04:
                case 0x0c:
                case 0x19:
                    return false;
                case 0x05:
                    return EnemyAttackFrame(t, 0, 4);
                case 0x07:
                    return EnemyAttackFrame(t, 0, 3);
                case 0x08:
                    return EnemyAttackFrame(t, 0, 6);
                case 0x09:
                    return EnemyAttackFrame(t, 0, 9) || EnemyAttackFrame(t, 0, 3);
                case 0x0d:
                case 0x0e:
                case 0x10:
                case 0x12:
                case 0x22:
                case 0x25:
                case 0x32:
                case 0x36:
                    return EnemyAttackFrame(t, 0, 3);
                case 0x11:
                case 0x13:
                    return EnemyAttackFrame(t, 0, 1);
                case 0x16:
                    return EnemyAttackFrame(t, 0, 7);
                case 0x1a:
                case 0x24:
                    return EnemyAttackFrame(t, 0, 4);
                case 0x1b:
                case 0x31:
                    return EnemyAttackFrame(t, 0, 5);
                case 0x1d:
                case 0x33:
                    return EnemyAttackFrame(t, 0, 2);
                case 0x34:
                    return EnemyAttackFrame(t, 1, 3);
                default:
                    return FrameCounter % 9 == 0;
            }
        }

        private static bool EnemyAttackFrame(EnemyRecord t, int attack, int frame) =>
            t.Attacking != 0 && t.AnimTick == 0 && t.AtkIdx == attack && t.Frame == frame;

        /// <summary>Puts a new enemy in the first free record (never the dormant Primus slots).</summary>
        public int EnemySpawn(int type, int x, int y, int mode, int hp, int range, int speed)
        {
            int k;
            for (k = 1; k < EnemyCount; k++)
            {
                if (k != PrimusFlyerSlot && k != PrimusWalkerSlot && Enemies[k].State == EnemyRecord.Free)
                {
                    break;
                }
            }
            if (k >= EnemyCount)
            {
                return 0;
            }
            var r = Enemies[k];
            var t = type < EnemyTypeTable.Count ? EnemyTypes[type] : null;
            r.State = EnemyRecord.Alive;
            r.X = x & 0xffff;
            r.Y = y & 0xffff;
            r.Type = type;
            r.Mode = mode & 0xffff;
            r.Hp = hp & 0xffff;
            r.Range = range & 0xffff;
            r.Speed = speed & 0xffff;
            r.DiffMask = Difficulty;
            if (t != null)
            {
                r.W = t.W;
                r.H = t.H;
                r.NFrames = t.Frames & 0xff;
                r.NDieFrames = t.DieFrames & 0xff;
                r.Flags = t.Flags;
                r.Weapon = t.Weapon;
            }
            r.Dir = 1;
            r.Attacking = 0;
            r.AtkLatch = 0;
            r.AtkIdx = 0;
            r.AnimTick = 0;
            r.Aux = 0;
            r.Damage = 0;
            r.Frame = 0;
            r.Animate = 0;
            r.AtkCooldown = 0;
            r.FlinchAcc = 0;
            EnemyOccupancy(k, mark: true, useCurrent: true);
            return k;
        }
    }
}
