namespace Thexder.Core.Game
{
    // The enemy movement engine and its map helpers.
    internal sealed partial class ThexderGame
    {
        private static readonly int[] EnemyDir16Frame = { 8, 7, 6, 5, 4, 3, 2, 1, 0, 15, 14, 13, 12, 11, 10, 9 };
        private static readonly int[] EnemyTriTurretFrame = { 4, 4, 3, 3, 2, 2, 1, 1, 0, 0, 7, 7, 6, 6, 5, 5 };
        private static readonly int[] EnemyPulsarCzarFrame = { 8, 8, 6, 6, 4, 4, 2, 2, 0, 0, 14, 14, 12, 12, 10, 10 };
        private static readonly int[] EnemyClockwise = { 4, 6, 2, 10, 8, 9, 1, 5 };
        private static readonly int[] EnemyCrawlerTurns = { 4, 2, 1 };

        /// <summary>One movement / attack-animation step for an enemy (or a platform).</summary>
        public void EnemyAI(EnemyRecord r, ref int nx, ref int ny, ref int dir, ref int frame)
        {
            int x = r.X, y = r.Y, w = r.W, h = r.H, speed = r.Speed;
            int tcx = (ushort)((w >> 1) + x);
            int q = h >> 2;
            if (q <= h - 3)
            {
                q = h - 3;
            }
            int tcy = (ushort)(q + y);
            int pw = FormW[Player.Form], ph = FormH[Player.Form];
            int pcx = EnemyPlayerCenterX, pcy = EnemyPlayerCenterY;
            bool overlap = Math.Max(x, Player.X) < Math.Min(x + w, Player.X + pw) && Math.Max(y, Player.Y) < Math.Min(y + h, Player.Y + ph);

            EnemyType? type = null;
            bool isPlatform = false;
            int mode = (short)r.Mode;
            if (r.Type < EnemyTypeTable.Count)
            {
                type = EnemyTypes[r.Type];
                if (type != null && r.State == EnemyRecord.Alive && r.Attacking != 0 && type.Attacks[r.AtkIdx & 1].Mode != -1)
                {
                    mode = type.Attacks[r.AtkIdx & 1].Mode;
                }
            }
            else
            {
                if (speed != 1)
                {
                    speed = (speed - 1) & 0xffff;
                }
                isPlatform = true;
            }
            r.Animate = 1;
            bool t = r.State == EnemyRecord.Alive;
            int animDelay = Math.Max(1, type?.AnimDelay ?? 1);
            int attackFrames = type?.Attacks[r.AtkIdx & 1].Frames ?? 0;

            (int X, int Y) Move(int d) => MoveBox(x, y, d & 0xffff, speed, w, h, t, isPlatform);

            void FacePlayer(ref int d)
            {
                if (tcx < pcx)
                {
                    d = 2;
                }
                else if (pcx < tcx)
                {
                    d = 1;
                }
            }

            void Chase(ref int d)
            {
                if (tcx < pcx)
                {
                    d = 2;
                }
                if (pcx < tcx)
                {
                    d = 1;
                }
                if (tcy < pcy)
                {
                    d |= 8;
                }
                if (pcy < tcy)
                {
                    d |= 4;
                }
            }

            // The attack modes' common frame step: advance with the type's delay, or start the attack facing the player
            void AttackStep(ref int f, ref int d, bool face)
            {
                if (r.Attacking == 0 || r.AtkLatch != 0)
                {
                    r.AnimTick = (r.AnimTick + 1) & 0xffff;
                    if (r.AnimTick % animDelay == 0)
                    {
                        r.AnimTick = 0;
                        f = (f + 1) & 0xffff;
                    }
                }
                else
                {
                    r.AtkLatch = 1;
                    f = 0;
                    if (face)
                    {
                        FacePlayer(ref d);
                    }
                }
            }

            switch (mode)
            {
                case 0:
                {
                    // Stationary / aiming
                    bool aim = false;
                    if (Player.Form == PlayerState.Plane && (r.Type == 1 || r.Type == 0x27) && r.State == EnemyRecord.Alive)
                    {
                        aim = true;
                    }
                    else if (r.Type == 0x23 && r.State == EnemyRecord.Alive)
                    {
                        if (r.Frame % 3 == 1)
                        {
                            frame = (frame + 1) & 0xffff; // Recoil frame after a launch
                            r.Animate = 0;
                            r.Frame = frame;
                        }
                        else
                        {
                            aim = true;
                        }
                    }
                    if (aim)
                    {
                        int o = Dir16(tcx, tcy, pcx, pcy);
                        frame = EnemyDir16Frame[o];
                        r.Animate = 0;
                        if (r.Type == 0x23)
                        {
                            frame *= 3;
                        }
                        else if (r.Type == 0x27)
                        {
                            frame = EnemyTriTurretFrame[o];
                        }
                        r.Frame = frame;
                    }
                    if (type != null && !EnemySameBitmap(type.SprL, type.SprR))
                    {
                        FacePlayer(ref dir);
                    }
                    break;
                }

                case 1:
                    // Homing flyer
                    if (r.Type == EnemyTypeTable.FlyingSphinx && r.Timer > 150)
                    {
                        r.Timer = 0;
                        r.Flags |= EnemyRecord.FlagSphinxTransition; // Lands and becomes a statue again
                        frame = 2;
                        r.Frame = 2;
                        dir = 8;
                        (nx, ny) = Move(8);
                    }
                    else
                    {
                        if (!overlap && r.Aux == 0)
                        {
                            Chase(ref dir);
                        }
                        else if (r.Aux == 0)
                        {
                            dir = pcx < tcx ? 2 : 1; // Touching the player: back off
                            if (tcy < pcy)
                            {
                                dir |= 4;
                            }
                            if (pcy < tcy)
                            {
                                dir |= 8;
                            }
                            if (type != null && type.TurnFrames != 0)
                            {
                                r.Aux = r.W;
                            }
                        }
                        else
                        {
                            r.Aux = (r.Aux - speed) & 0xffff; // Wraps when speed does not divide w
                        }
                        (nx, ny) = Move(dir);
                    }
                    break;

                case 2:
                    // Erratic flyer
                    if (Random.Next() < 0x2aaa)
                    {
                        Chase(ref dir);
                    }
                    else
                    {
                        int roll = Random.Next();
                        if (roll < 0x2aaa)
                        {
                            dir = 2;
                        }
                        else if (roll < 0x5555)
                        {
                            dir = 1;
                        }
                        roll = Random.Next();
                        if (roll < 0x2aaa)
                        {
                            dir |= 8;
                        }
                        else if (roll < 0x5555)
                        {
                            dir |= 4;
                        }
                    }
                    (nx, ny) = Move(dir);
                    break;

                case 3:
                    // Ground walker
                    if (r.Attacking == 0 || frame == 0)
                    {
                        if (!overlap)
                        {
                            if (tcx < pcx)
                            {
                                dir = 2;
                            }
                            if (pcx < tcx)
                            {
                                dir = 1;
                            }
                            dir |= 8;
                        }
                        else if (type == null)
                        {
                            dir |= 8;
                        }
                        else
                        {
                            if (type.TurnFrames == 0)
                            {
                                // Cannot turn: walks away from the player
                                if (tcx < pcx)
                                {
                                    dir = 1;
                                }
                                if (pcx < tcx)
                                {
                                    dir = 2;
                                }
                            }
                            dir |= 8;
                        }
                    }
                    (nx, ny) = Move(dir);
                    break;

                case 4:
                    // Gravity only; the Sphinx statue rises while it transforms
                    if (r.Type == EnemyTypeTable.SphinxStatue)
                    {
                        if (r.Timer > 150)
                        {
                            r.Timer = 0;
                            r.Flags |= EnemyRecord.FlagSphinxTransition;
                            frame = 0;
                            speed = 6;
                            r.Frame = 0;
                        }
                        dir = (r.Flags & EnemyRecord.FlagSphinxTransition) == 0 ? 8 : 4;
                        (nx, ny) = Move(dir);
                        FacePlayer(ref dir);
                    }
                    else
                    {
                        dir = 8;
                        (nx, ny) = Move(8);
                    }
                    break;

                case 5:
                {
                    // Fall, and slide one body diagonally off ledges
                    dir = 8;
                    var (mx, my) = Move(8);
                    if (my == y)
                    {
                        dir = 9;
                        var (ax, ay) = EnemySlideProbe(x, y, 9, w, h);
                        bool left = ax != x && ay != y;
                        dir = 10;
                        var (bx, by) = EnemySlideProbe(x, y, 10, w, h);
                        bool right = bx != x && by != y;
                        (mx, my) = (x, y);
                        if (left && right)
                        {
                            (mx, my) = Random.Next() < 15000 ? (ax, ay) : (bx, by);
                        }
                        else if (left)
                        {
                            (mx, my) = (ax, ay);
                        }
                        else if (right)
                        {
                            (mx, my) = (bx, by);
                        }
                    }
                    (nx, ny) = (mx, my);
                    break;
                }

                case 6:
                {
                    // Diagonal-only homing
                    if (!overlap)
                    {
                        if (tcx < pcx)
                        {
                            dir = 2;
                        }
                        else if (pcx < tcx)
                        {
                            dir = 1;
                        }
                        if (tcy < pcy)
                        {
                            dir |= 8;
                        }
                        else if (pcy < tcy)
                        {
                            dir |= 4;
                        }
                    }
                    else if (type != null && type.TurnFrames == 0)
                    {
                        dir = pcx < tcx ? 2 : 1;
                        if (tcy < pcy)
                        {
                            dir |= 4;
                        }
                        else if (pcy < tcy)
                        {
                            dir |= 8;
                        }
                    }
                    var (mx, my) = Move(dir);
                    if (mx != x && my != y)
                    {
                        (nx, ny) = (mx, my);
                    }
                    break;
                }

                case 7:
                case 8:
                case 9:
                case 10:
                    // Patrol: reverse when blocked
                    if (r.Type == EnemyTypeTable.FlyingSphinx && r.Timer > 150)
                    {
                        r.Timer = 0;
                        r.Flags |= EnemyRecord.FlagSphinxTransition;
                        frame = 2;
                        r.Frame = 2;
                        dir = 8;
                        (nx, ny) = MoveBox(x, y, 8, type?.Speed ?? speed, w, h, t, isPlatform);
                    }
                    else
                    {
                        var (mx, my) = Move(dir);
                        if (mx == x && my == y)
                        {
                            dir = ReverseDir(dir);
                            if (r.Type == 0x6e && (r.Flags & EnemyRecord.PlatformSwitchedOn) != 0)
                            {
                                r.Flags &= ~EnemyRecord.PlatformSwitchedOn; // Switched platform: stops until switched again
                            }
                            else
                            {
                                (mx, my) = Move(dir);
                            }
                        }
                        (nx, ny) = (mx, my);
                    }
                    break;

                case 11:
                case 12:
                {
                    // Wall crawlers: 11 keeps the wall clockwise of its motion, 12 counter-clockwise; one cell per tick
                    int d = (short)dir;
                    int mx = nx, my = ny;
                    int rot = (short)r.Mode == 12 ? 1 : 2;
                    if (!EnemyProbeTurned(d, rot, mx, my, w, h, false, isPlatform))
                    {
                        bool near = EnemyNearWall(mx, my, w, h);
                        EnemyStep(d, rot, w, h, ref mx, ref my);
                        if (near)
                        {
                            dir = RotateDir(d, rot);
                        }
                    }
                    else
                    {
                        int k;
                        for (k = 0; k < 3; k++)
                        {
                            if (!EnemyProbeTurned(d, EnemyCrawlerTurns[k], x, y, w, h, false, isPlatform))
                            {
                                break;
                            }
                        }
                        int turn = k == 3 ? 8 : EnemyCrawlerTurns[k];
                        EnemyStep(d, turn, w, h, ref mx, ref my);
                        dir = RotateDir(d, turn);
                    }
                    (nx, ny) = (mx, my);
                    break;
                }

                case 13:
                {
                    bool fallOnly = false;
                    r.Animate = 0;
                    int d = dir;
                    int md = d;
                    if (r.Attacking != 0 && r.AtkLatch == 0)
                    {
                        r.AtkLatch = 1;
                    }
                    if (frame == 0)
                    {
                        r.Flags |= EnemyRecord.FlagHopping; // On the ground at frame 0: get ready
                        FacePlayer(ref d);
                        md = d;
                    }
                    int a = r.Aux;
                    int hop = type?.HopHeight ?? 4;
                    if (a < hop)
                    {
                        if (a == 0)
                        {
                            if ((r.Flags & EnemyRecord.FlagHopping) == 0 || r.NFrames - frame != 2)
                            {
                                fallOnly = true;
                                md = 8;
                            }
                            else
                            {
                                d = (d & ~8) | 4; // Take-off frame
                                md = d;
                                r.Aux = speed;
                            }
                        }
                        else if ((d & 4) == 0)
                        {
                            r.Aux = a - speed < 1 ? 0 : a - speed;
                        }
                        else
                        {
                            r.Aux = (speed + a) & 0xffff;
                        }
                    }
                    else
                    {
                        d = (d & ~4) | 8; // Apex reached: come down
                        md = d;
                        r.Flags &= ~EnemyRecord.FlagHopping;
                        r.Aux = a - speed < 1 ? 0 : a - speed;
                    }
                    var (mx, my) = Move(md);
                    if (mx == x && my == y && !fallOnly)
                    {
                        if ((md & 4) == 0)
                        {
                            r.Aux = 0; // Landed / blocked
                            frame = (r.NFrames - 1) & 0xffff;
                        }
                        else
                        {
                            d = (d & ~4) | 8; // Head bump
                            r.Aux = (r.Aux - speed) & 0xffff;
                            (mx, my) = Move(d);
                        }
                    }
                    if (!BlockedD(mx, my, type?.W ?? w, type?.H ?? h, t, isPlatform))
                    {
                        frame = (r.NFrames - 1) & 0xffff; // Airborne: last frame
                    }
                    else if ((r.Flags & EnemyRecord.FlagHopping) == 0)
                    {
                        frame = (frame - 1) & 0xffff;
                        if (r.NFrames - 1 <= frame)
                        {
                            frame = 0;
                        }
                    }
                    else
                    {
                        frame = (frame + 1) & 0xffff;
                        if (r.NFrames - 1 <= frame)
                        {
                            frame = 0;
                        }
                    }
                    r.Frame = frame;
                    if (frame == 0)
                    {
                        r.AtkLatch = 0;
                        FacePlayer(ref d);
                    }
                    dir = d;
                    (nx, ny) = (mx, my);
                    break;
                }

                case 14:
                    // Inchworm: moves on animation frames 1..6
                    if (frame == 0)
                    {
                        if (tcx < pcx)
                        {
                            dir = 2;
                        }
                        if (pcx < tcx)
                        {
                            dir = 1;
                        }
                        dir |= 8;
                    }
                    if (frame != 0 && frame < 7)
                    {
                        (nx, ny) = Move(dir);
                    }
                    break;

                case 15:
                {
                    // "hold" attack: plays forward, holds the last frame HopHeight ticks, plays backward; no movement
                    int d = dir;
                    if (r.Attacking == 0 || r.AtkLatch != 0)
                    {
                        if ((r.Flags & EnemyRecord.FlagHopping) == 0)
                        {
                            frame = (frame - 1) & 0xffff;
                            if (r.Frame == 0)
                            {
                                r.AtkLatch = 0;
                            }
                        }
                        else
                        {
                            int old = frame;
                            frame = (frame + 1) & 0xffff;
                            if (attackFrames <= frame)
                            {
                                frame = old;
                                if (r.Aux < (type?.HopHeight ?? 0))
                                {
                                    r.Aux++;
                                }
                                else
                                {
                                    r.Flags &= ~EnemyRecord.FlagHopping;
                                }
                            }
                        }
                    }
                    else
                    {
                        r.AtkLatch = 1;
                        frame = 0;
                        FacePlayer(ref d);
                        r.Flags |= EnemyRecord.FlagHopping;
                        r.Aux = 0;
                    }
                    r.Animate = 0;
                    r.Frame = frame;
                    dir = d;
                    break;
                }

                case 16:
                    if (r.Type != 0x14)
                    {
                        // Stand and attack
                        int d = dir;
                        AttackStep(ref frame, ref d, face: true);
                        r.Animate = 0;
                        if (attackFrames - frame == 1)
                        {
                            r.AtkLatch = 0;
                        }
                        r.Frame = frame;
                        dir = d;
                    }
                    else
                    {
                        // Pulsar Czar: aim one of 8 directions, then fire
                        if ((frame & 1) == 0)
                        {
                            frame = (frame + 1) & 0xffff;
                            r.AtkLatch = 0;
                        }
                        else
                        {
                            int o = Dir16(tcx, tcy, pcx, pcy);
                            if ((o & 1) != 0)
                            {
                                o--;
                            }
                            frame = EnemyPulsarCzarFrame[o];
                            r.AtkLatch = 1;
                        }
                        r.Animate = 0;
                        r.Frame = frame;
                    }
                    break;

                case 17:
                {
                    // Attack under gravity
                    int d = dir;
                    if (r.Attacking != 0 && r.AtkLatch == 0)
                    {
                        r.Aux = 0;
                    }
                    AttackStep(ref frame, ref d, face: true);
                    r.Animate = 0;
                    if (r.Type == 0x37 && frame > 4)
                    {
                        frame = 5; // Death Plumber holds frame 5 for 3 more ticks
                        if (r.Aux == 3)
                        {
                            r.AtkLatch = 0;
                        }
                        else
                        {
                            r.Aux++;
                        }
                    }
                    else if (attackFrames - frame == 1)
                    {
                        r.AtkLatch = 0;
                    }
                    r.Frame = frame;
                    (nx, ny) = Move(8);
                    dir = d;
                    break;
                }

                case 18:
                {
                    // Attack while advancing
                    int d = dir;
                    AttackStep(ref frame, ref d, face: true);
                    r.Animate = 0;
                    if (attackFrames - frame == 1)
                    {
                        r.AtkLatch = 0;
                    }
                    r.Frame = frame;
                    int m = 0;
                    if (!overlap)
                    {
                        if (tcx < pcx)
                        {
                            m = 2;
                        }
                        if (pcx < tcx)
                        {
                            m = 1;
                        }
                    }
                    (nx, ny) = Move(m | 8);
                    dir = d;
                    break;
                }

                case 19:
                    // Bug Bomb thrown by a Bug Breath: up-left for 4 frames, then down-left
                    dir = (frame < 4 ? 4 : 8) | 1;
                    if (frame != 0)
                    {
                        (nx, ny) = Move(dir);
                    }
                    frame = (frame + 1) & 0xffff;
                    r.Animate = 0;
                    r.Frame = frame;
                    break;

                case 20:
                    // Mega Mine launched by a Mega Gun: flies, then stops (~10 % a tick after 3 ticks) and hovers
                    r.Aux = (r.Aux + 1) & 0xffff;
                    if (dir != 0 && Random.Next() < 0xcc6 && r.Aux > 3)
                    {
                        dir = 0;
                    }
                    if (dir != 0)
                    {
                        (nx, ny) = Move(dir);
                    }
                    break;

                case 21:
                    // Primus Crawler: drives at the player, climbs ledges h + 1 cells high, crushes shoot-away blocks
                    if ((r.Flags & EnemyRecord.FlagHopping) == 0)
                    {
                        dir = pcx < tcx ? 1 : 2;
                        if (EnemyCanClimb(r, dir & 0xff))
                        {
                            r.Aux = 0;
                            r.Flags |= EnemyRecord.FlagHopping;
                            dir = 4;
                        }
                        else
                        {
                            dir |= 8;
                        }
                    }
                    else if (r.Aux < h + 1)
                    {
                        r.Aux = (speed + r.Aux) & 0xffff;
                        dir = 4;
                    }
                    else
                    {
                        r.Aux = 0;
                        dir = r.Dir;
                        r.Flags &= ~EnemyRecord.FlagHopping;
                    }
                    EnemyCrushBlocks(r, dir);
                    (nx, ny) = Move(dir);
                    break;

                case 22:
                {
                    // Hovering attack: the direction is fixed for the whole lunge
                    bool move = true;
                    if (r.Attacking == 0 || r.AtkLatch != 0)
                    {
                        r.AnimTick = (r.AnimTick + 1) & 0xffff;
                        if (r.AnimTick % animDelay == 0)
                        {
                            r.AnimTick = 0;
                            frame = (frame + 1) & 0xffff;
                        }
                        if (overlap)
                        {
                            move = false;
                        }
                    }
                    else
                    {
                        r.AtkLatch = 1;
                        frame = 0;
                        Chase(ref dir);
                    }
                    if (move)
                    {
                        (nx, ny) = Move(dir);
                    }
                    if (r.AnimTick - animDelay == -1 && attackFrames - frame == 1)
                    {
                        r.AtkLatch = 0;
                    }
                    r.Animate = 0;
                    r.Frame = frame;
                    break;
                }

                case 23:
                {
                    // Primus Flyer: horizontal flight, dives between rows 14 and 44
                    if ((r.Flags & EnemyRecord.FlagHopping) == 0 && r.Aux > 99)
                    {
                        dir |= 8;
                        r.Flags |= EnemyRecord.FlagHopping;
                    }
                    int mx = nx, my = ny;
                    if ((r.Flags & EnemyRecord.FlagHopping) == 0)
                    {
                        r.Aux = (r.Aux + Random.Next() % 3 + 1) & 0xffff;
                        (mx, my) = Move(dir);
                        if (mx == x && my == y)
                        {
                            dir = dir != 1 ? 1 : 2;
                            (mx, my) = Move(dir);
                        }
                    }
                    else
                    {
                        if ((dir & 8) == 0)
                        {
                            if (y > 14)
                            {
                                (mx, my) = Move(dir);
                            }
                        }
                        else if (y < 44)
                        {
                            (mx, my) = Move(dir);
                        }
                        if (mx == x && my == y)
                        {
                            if ((dir & 8) == 0)
                            {
                                dir &= ~4; // Back to horizontal flight
                                r.Flags &= ~EnemyRecord.FlagHopping;
                                r.Aux = 0;
                            }
                            else
                            {
                                dir = (dir & ~8) | 4; // Bottom: climb
                            }
                            (mx, my) = Move(dir);
                        }
                    }
                    switch (dir)
                    {
                        case 1:
                            frame = 8;
                            break;
                        case 2:
                            frame = 0;
                            break;
                        case 4:
                            frame = 4;
                            break;
                        case 5:
                            frame = 6;
                            break;
                        case 6:
                            frame = 2;
                            break;
                        case 8:
                            frame = 12;
                            break;
                        case 9:
                            frame = 10;
                            break;
                        case 10:
                            frame = 14;
                            break;
                    }
                    r.Animate = 0;
                    r.Frame = frame;
                    (nx, ny) = (mx, my);
                    break;
                }

                case 24:
                {
                    // Primus Walker: stands facing the player, leaps down-right every third hit
                    int jumping = r.Flags & EnemyRecord.FlagHopping;
                    int mx = nx, my = ny;
                    if (jumping == 0 && r.Aux != 0 && r.Aux % 3 == 0 && y < 58)
                    {
                        r.Flags |= EnemyRecord.FlagHopping;
                        dir = 10;
                        (mx, my) = Move(10);
                        if (mx == x && my == y)
                        {
                            r.Flags &= ~EnemyRecord.FlagHopping;
                            dir = 1;
                            r.Aux = (r.Aux + 1) & 0xffff;
                        }
                    }
                    else if (jumping == 0)
                    {
                        frame = 12;
                        FacePlayer(ref dir);
                        r.Animate = 0;
                        r.Frame = frame;
                    }
                    else
                    {
                        dir = 10;
                        (mx, my) = Move(10);
                        if (mx == x && my == y)
                        {
                            r.Flags &= ~(EnemyRecord.FlagPrimusFalling | EnemyRecord.FlagHopping);
                            dir = 1;
                            r.Aux = (r.Aux + 1) & 0xffff;
                        }
                        else if ((r.Flags & EnemyRecord.FlagPrimusFalling) == 0 && y < my)
                        {
                            r.Flags |= EnemyRecord.FlagPrimusFalling;
                        }
                        else if ((r.Flags & EnemyRecord.FlagPrimusFalling) != 0 && my == y)
                        {
                            r.Flags &= ~(EnemyRecord.FlagPrimusFalling | EnemyRecord.FlagHopping);
                            dir = 1;
                            r.Aux = (r.Aux + 1) & 0xffff;
                        }
                    }
                    (nx, ny) = (mx, my);
                    break;
                }

                case 25:
                    // Primus Walker attack: one of 5 aim poses of 3 frames
                    if (r.Attacking == 0 || r.AtkLatch != 0)
                    {
                        r.AnimTick = (r.AnimTick + 1) & 0xffff;
                        if (r.AnimTick % animDelay == 0)
                        {
                            r.AnimTick = 0;
                            frame = (frame + 1) & 0xffff;
                        }
                    }
                    else
                    {
                        r.AtkLatch = 1;
                        frame = Random.Next() % 5 * 3;
                    }
                    r.Animate = 0;
                    if ((frame + 1) % 3 == 0)
                    {
                        r.AtkLatch = 0;
                    }
                    r.Frame = frame;
                    break;
            }

            switch (r.Type)
            {
                case 0x04:
                    r.Range = 0x280; // A Mine, once woken, stays active
                    break;
                case 0x0a:
                    r.Range = 50;
                    break;
                case EnemyTypeTable.FlyingSphinx:
                case EnemyTypeTable.SphinxStatue:
                    r.Range = 0x280;
                    r.Timer = (r.Timer + 1) & 0xffff;
                    break;
            }
        }

        public static int Dir16(int ax, int ay, int bx, int by)
        {
            int dx = ax - bx;
            if (dx == 0)
            {
                dx = 1;
            }
            float s = (float)((double)(ay - by) / dx);
            int n = bx <= ax ? 8 : 0;
            if (s > -4.0f)
            {
                n++;
            }
            if (s > -1.4)
            {
                n++;
            }
            if (s > -0.714)
            {
                n++;
            }
            if (s > -0.25)
            {
                n++;
            }
            if (s > 0.25)
            {
                n++;
            }
            if (s > 0.714)
            {
                n++;
            }
            if (s > 1.4)
            {
                n++;
            }
            if (s > 4.0)
            {
                n++;
            }
            return n & 15;
        }

        public static int ReverseDir(int dir) => (dir & 0xf) switch
        {
            1 => 2,
            2 => 1,
            4 => 8,
            5 => 10,
            6 => 9,
            8 => 4,
            9 => 6,
            10 => 5,
            _ => dir,
        };

        public static int RotateDir(int a, int b)
        {
            int ia = Array.IndexOf(EnemyClockwise, (int)(short)a);
            int ib = Array.IndexOf(EnemyClockwise, (int)(short)b);
            return EnemyClockwise[((ia < 0 ? 8 : ia) + (ib < 0 ? 8 : ib)) & 7];
        }

        /// <summary>The edge probe of a direction; other values count as blocked.</summary>
        private bool EnemyProbe(int d, int x, int y, int w, int h, bool t, bool raw) => (d & 0xffff) switch
        {
            1 => BlockedL(x, y, w, h, t, raw),
            2 => BlockedR(x, y, w, h, t, raw),
            4 => BlockedU(x, y, w, h, t, raw),
            5 => BlockedUL(x, y, w, h, t, raw),
            6 => BlockedUR(x, y, w, h, t, raw),
            8 => BlockedD(x, y, w, h, t, raw),
            9 => BlockedDL(x, y, w, h, t, raw),
            10 => BlockedDR(x, y, w, h, t, raw),
            _ => true,
        };

        private bool EnemyProbeTurned(int d, int turn, int x, int y, int w, int h, bool t, bool raw) =>
            EnemyProbe(RotateDir(d, turn), x, y, w, h, t, raw);

        private bool EnemyNearWall(int x, int y, int w, int h) =>
            BlockedR(x, y, w, h, false, false) || BlockedUR(x, y, w, h, false, false) || BlockedU(x, y, w, h, false, false)
            || BlockedUL(x, y, w, h, false, false) || BlockedL(x, y, w, h, false, false) || BlockedDL(x, y, w, h, false, false)
            || BlockedD(x, y, w, h, false, false) || BlockedDR(x, y, w, h, false, false);

        private void EnemyStep(int d, int turn, int w, int h, ref int x, ref int y)
        {
            int qx = x, qy = y;
            switch (RotateDir(d, turn))
            {
                case 1:
                    qx--;
                    break;
                case 2:
                    qx++;
                    break;
                case 4:
                    qy--;
                    break;
                case 5:
                    qx--;
                    qy--;
                    break;
                case 6:
                    qx++;
                    qy--;
                    break;
                case 8:
                    qy++;
                    break;
                case 9:
                    qx--;
                    qy++;
                    break;
                case 10:
                    qx++;
                    qy++;
                    break;
            }
            if (MapW - w < qx)
            {
                qx = MapW - w;
            }
            if (qx < 0)
            {
                qx = 0;
            }
            if (MapH - h < qy)
            {
                qy = MapH - h;
            }
            if (qy < 0)
            {
                qy = 0;
            }
            if (!EnemyBoxBlocked(qx, qy, w, h, false, true))
            {
                x = qx;
                y = qy;
            }
        }

        private bool EnemyBoxBlocked(int x, int y, int w, int h, bool t, bool raw)
        {
            for (int row = 0; row < h; row++)
            {
                for (int col = 0; col < w; col++)
                {
                    if (CellBlocked(Map.Wrap16((short)(y + row) * MapW + x + col), t, raw) != 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// The box one full body down-left (9) or down-right (10); returns (x, y) unchanged when that leaves the map or
        /// holds a solid tile, an object or an occupant.
        /// </summary>
        private (int X, int Y) EnemySlideProbe(int x, int y, int d, int w, int h)
        {
            int tx, ty = y + h;
            if (d == 9)
            {
                tx = x - w;
                if (tx < 1 || w + tx > MapW - 1 || h + ty > MapH - 1)
                {
                    return (x, y);
                }
            }
            else if (d == 10)
            {
                tx = x + w;
                if (tx == 0 || w + tx > MapW - 1 || h + ty > MapH - 1)
                {
                    return (x, y);
                }
            }
            else
            {
                return (x, y);
            }
            for (int row = ty; row < ty + h; row++)
            {
                for (int col = tx; col < tx + w; col++)
                {
                    int c = row * MapW + col;
                    if (Map.IsSolid(c) || (Map.B3(c) & 0x3f) != 0 || (Map.B2(c) & 0x7f) != 0)
                    {
                        return (x, y);
                    }
                }
            }
            return (tx, ty);
        }

        /// <summary>
        /// The cell in front at the row above the enemy is solid (not shoot-away) and the column above it is clear up to
        /// the enemy's height.
        /// </summary>
        private bool EnemyCanClimb(EnemyRecord r, int d)
        {
            int cx = r.X + ((d & 1) != 0 ? -1 : (short)r.W);
            if (cx > MapW - 1)
            {
                cx = MapW;
            }
            int row = r.Y - 1;
            if (row < 1)
            {
                row = 0;
            }
            if (!EnemySolidWall(Map.Wrap16((short)row * MapW + (short)cx)))
            {
                return false;
            }
            int limit = (short)row - r.H;
            for (int k = (short)(row - 1); limit < k; k = (short)(k - 1))
            {
                if (EnemySolidWall(Map.Wrap16(k * MapW + (short)cx)))
                {
                    return false;
                }
            }
            return true;
        }

        private bool EnemySolidWall(int c) => c < Map.Count && Map.Tile(c) != 0 && (Map.B3(c) & 0xc0) == 0 && (Map.B2(c) & 0x80) == 0;

        /// <summary>
        /// Removes shoot-away cells in front of the enemy (speed columns) and in the row above it, with a crumble sound.
        /// </summary>
        private void EnemyCrushBlocks(EnemyRecord r, int d)
        {
            bool crushed = false;
            if ((d & 3) != 0)
            {
                for (int k = 0; k < r.Speed; k++)
                {
                    int col;
                    if ((d & 1) == 0)
                    {
                        col = r.X + r.W + k;
                        if (col > MapW - 1)
                        {
                            col = MapW;
                        }
                    }
                    else
                    {
                        col = r.X - k - 1;
                        if (col < 1)
                        {
                            col = 0;
                        }
                    }
                    int row = (r.Y - 1) & 0xffff;
                    for (; row < r.H + r.Y; row = (row + 1) & 0xffff)
                    {
                        crushed |= EnemyCrushCell(Map.Wrap16((short)row * MapW + (short)col), col, row);
                    }
                }
            }
            for (int col = r.X; col < r.X + r.W; col++)
            {
                crushed |= EnemyCrushCell(Map.Wrap16(r.Y * MapW + col - MapW), col, r.Y - 1);
            }
            if (crushed)
            {
                Sounds.PlayAt((Random.Next() & 1) == 0 ? 0 : 0x3e, 0, -850);
            }
        }

        private bool EnemyCrushCell(int c, int x, int y)
        {
            if (c >= Map.Count || Map.Tile(c) == 0 || (Map.B2(c) & 0x80) == 0)
            {
                return false;
            }
            Map.SetTile(c, 0);
            Map.SetB2(c, (byte)(Map.B2(c) & 0x7f));
            MapPlot(x, y, 1, 1, 0);
            return true;
        }
    }
}
