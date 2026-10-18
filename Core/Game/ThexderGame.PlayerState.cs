using Thexder.Core.Assets;

namespace Thexder.Core.Game
{
    // Player animation, drawing, damage, hazards and pickups.
    internal sealed partial class ThexderGame
    {
        /// <summary>A sprite set: strip resource name and frame box in cells.</summary>
        private readonly record struct SpriteSet(string? Name, int W, int H);

        // spriteSet -> strip. 6..14 transforms facing left, 15..23 facing right (index form*3+target).
        private static readonly SpriteSet[] PlayerSprites =
        {
            new("ThexderL", 4, 6), new("Plane", 5, 5), new("ThexderTankL", 4, 3),
            new("ThexderR", 4, 6), new("Plane", 5, 5), new("ThexderTankR", 4, 3),
            new(null, 0, 0), new("W2PLeft", 5, 6), new("W2CLeft", 4, 6), new("W2PLeft", 5, 6), new(null, 0, 0),
            new("P2CLeft", 5, 5), new("W2CLeft", 4, 6), new("P2CLeft", 5, 5), new(null, 0, 0),
            new(null, 0, 0), new("W2PRight", 5, 6), new("W2CRight", 4, 6), new("W2PRight", 5, 6), new(null, 0, 0),
            new("P2CRight", 5, 5), new("W2CRight", 4, 6), new("P2CRight", 5, 5), new(null, 0, 0),
            new("ThexderShootL", 4, 6), new("ThexderShootR", 4, 6),
            new("ThexderTurn", 4, 6), new(null, 0, 0), new("ThexderTankTurn", 4, 3),
            new("ThexderFlinchL", 4, 6), new(null, 0, 0), new("ThexderTankFlinchL", 4, 3),
            new("ThexderFlinchR", 4, 6), new(null, 0, 0), new("ThexderTankFlinchR", 4, 3),
            new("ThexderDie", 4, 6), new("PlaneDie", 5, 5), new("ThexderTankDie", 4, 3),
            new("ThexderJumpL", 4, 6), new(null, 0, 0), new("ThexderTankLandL", 4, 3),
            new("ThexderJumpR", 4, 6), new(null, 0, 0), new("ThexderTankLandR", 4, 3),
            new("ThexderExpressions", 4, 6),
            new("ThexderTransport", 4, 6), new(null, 0, 0), new("ThexderTankTransport", 4, 3),
        };

        // planeFrame[dir]: 0 right, 2 up-right, 4 up, 6 up-left, 8 left, 10 down-left, 12 down, 14 down-right.
        private static readonly int[] PlaneFrame = { 0, 8, 0, 0, 4, 6, 2, 0, 12, 10, 14, 0, 0, 0, 0, 0 };

        private int prevBoxX, prevBoxY, prevBoxW, prevBoxH;
        private int lastAnimX;

        // The frame drawn last, for the pixel-exact player hit test (framePtr / spriteStride).
        private IndexedImage? drawnStrip;
        private int drawnFrameX, drawnFrameY;

        private IndexedImage? TurnSprite(int form) => form == PlayerState.Plane ? null : Art.Sprite(PlayerSprites[0x1a + form].Name);

        private IndexedImage? TransportSprite(int form) => form == PlayerState.Plane ? null : Art.Sprite(PlayerSprites[0x2d + form].Name);

        private void SetSpriteSet(int mode)
        {
            var p = Player;
            if ((p.StateFlags & PlayerState.FlagTransforming) != 0)
            {
                p.SpriteSet = p.Form * 3 + p.TransformTarget + 6 + (p.Facing == 2 ? 9 : 0);
                return;
            }
            p.SpriteSet = mode switch
            {
                1 => p.Form + (p.Facing != 1 ? 0x29 : 0x26),
                2 => p.Form + 0x1a,
                3 => p.Form + (p.Facing != 1 ? 0x20 : 0x1d),
                4 => p.Form + 0x23,
                5 => 0x2c,
                6 => p.Form + 0x2d,
                _ => p.Form + (p.Facing != 1 ? 3 : 0),
            };
        }

        /// <summary>Picks the animation and re-stamps the occupancy marks.</summary>
        private void PlayerAnimate()
        {
            var p = Player;
            int mode = 0;
            if ((p.StateFlags & PlayerState.FlagTransforming) != 0)
            {
                SetSpriteSet(0);
            }
            else if ((p.StateFlags & PlayerState.FlagTurning) != 0)
            {
                SetSpriteSet(2);
            }
            else if ((p.StateFlags & PlayerState.FlagDying) != 0)
            {
                SetSpriteSet(4);
            }
            else if ((p.StateFlags & 0x30) != 0)
            {
                SetSpriteSet(6);
            }
            else if ((p.StateFlags & PlayerState.FlagGoingHome) != 0)
            {
                SetSpriteSet(5);
            }
            else
            {
                if (p.Form == PlayerState.Walker)
                {
                    if (p.Frame > 12)
                    {
                        p.Frame = 0;
                    }
                    p.Frame += 2;
                    if (p.Frame > 11)
                    {
                        p.Frame = 0;
                    }
                    if (p.LastDir == 0)
                    {
                        p.Frame = 12;
                    }
                    else if (((p.LastDir & 8) != 0 && p.Jumping != 0) || p.Falling != 0)
                    {
                        mode = 1;
                        p.Frame = Random.Next() % 2 == 0 ? 7 : 6;
                    }
                    else if ((p.LastDir & 4) != 0 && p.Jumping != 0)
                    {
                        mode = 1;
                        p.Frame = Random.Next() % 2 == 0 ? 7 : 6;
                    }
                    else if (p.X == lastAnimX)
                    {
                        p.Frame = 12; // Pushing against a wall
                    }
                    if (p.Landed != 0)
                    {
                        p.Frame = 2;
                        mode = 1;
                    }
                    int bx = prevBoxX, bw = prevBoxW;
                    BodyIn(ref bx, ref bw);
                    if (BlockedD(bx, prevBoxY, bw, prevBoxH, true, false) && OnSlideFloor(bx, bw))
                    {
                        mode = 1;
                        p.Frame = 1;
                    }
                    if ((p.StateFlags & PlayerState.FlagFlinch) != 0)
                    {
                        if (mode == 0)
                        {
                            p.Frame = 0;
                            mode = 3;
                        }
                        else if (mode == 1 && (p.Frame == 6 || p.Frame == 7))
                        {
                            p.Frame = 8;
                        }
                    }
                }
                else if (p.Form == PlayerState.Plane)
                {
                    p.Frame = PlaneFrame[p.LastDir & 0xf];
                }
                else if (p.Form == PlayerState.Tank)
                {
                    p.Frame++;
                    if (p.Frame > 2)
                    {
                        p.Frame = 0;
                    }
                    if (p.Falling == 0)
                    {
                        if ((p.StateFlags & PlayerState.FlagFlinch) != 0)
                        {
                            mode = 3;
                            p.Frame = 0;
                        }
                    }
                    else
                    {
                        mode = 1;
                        p.Frame = Random.Next() % 2 == 0 ? 1 : 0;
                    }
                }
                SetSpriteSet(mode);
                p.StateFlags &= ~PlayerState.FlagFlinch;
            }
            lastAnimX = p.X;

            // Map dot: clear the previous box, then mark the current one.
            for (int row = prevBoxY; row < prevBoxY + prevBoxH; row++)
            {
                for (int col = prevBoxX; col < prevBoxX + prevBoxW; col++)
                {
                    if (col < MapW && col >= 0 && row >= 0 && row * MapW < Map.Count)
                    {
                        MapSurface.Pixels[row * MapW + col] = 0;
                    }
                }
            }
            for (int row = p.Y; row < p.Y + FormH[p.Form]; row++)
            {
                for (int col = p.X; col < p.X + FormW[p.Form]; col++)
                {
                    if (col < MapW && row * MapW < Map.Count)
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
            if (NetActive)
            {
                NetStampRemoteMarks();
            }
        }

        private void DrawPlayerSprite(PlayerState p, byte[] remap)
        {
            var set = p.SpriteSet >= 0 && p.SpriteSet < PlayerSprites.Length ? PlayerSprites[p.SpriteSet] : default;
            int f = p.Frame;
            if (p.SpriteSet >= 6 && p.SpriteSet <= 23 && TransformReversed[(p.SpriteSet - 6) % 9])
            {
                f = 11 - f;
            }
            var strip = Art.Sprite(set.Name);
            if (strip != null)
            {
                DrawSpriteCells(strip, p.X, p.Y, set.W, set.H, f, remap);
                if (ReferenceEquals(p, Player))
                {
                    p.SpriteStride = strip.Width;
                    drawnStrip = strip;
                    (drawnFrameX, drawnFrameY) = Engine.FrameBuffer.FrameOrigin(strip, set.W * S, set.H * S, f);
                }
            }
        }

        /// <summary>Draws the robot and advances its state animations.</summary>
        private void PlayerDraw()
        {
            var p = Player;
            byte[] remap = Art.Tints.Identity;
            if (p.HitFlash != 0)
            {
                p.HitFlash--;
                remap = Art.Tints.Hit;
            }
            else if (NetActive)
            {
                // Network play: the player's colour instead of the armour tints.
                remap = Art.Tints.PlayerColour(NetMode == 2 ? netLocalColour : 0);
            }
            else if (Inv.IsActive(ItemType.MegaArmor))
            {
                remap = Art.Tints.Gold;
            }
            else if (Inv.IsActive(ItemType.ShotArmor))
            {
                remap = Art.Tints.Cyan;
            }
            else if (Inv.IsActive(ItemType.LavaArmor))
            {
                remap = Art.Tints.Magenta;
            }
            else if (Inv.IsActive(ItemType.AcidArmor))
            {
                remap = Art.Tints.LightGreen;
            }

            SceneTag(EntityPlayer);
            DrawPlayerSprite(p, remap);
            SceneTag(0);

            uint fl = p.StateFlags;
            if ((fl & PlayerState.FlagTransforming) != 0)
            {
                if (++p.Frame > 11)
                {
                    TransformComplete();
                }
            }
            else if ((fl & PlayerState.FlagTurning) != 0)
            {
                if (p.Facing == 1)
                {
                    int lim = (fl & 0x38) != 0 ? 3 : 0;
                    if (p.Frame > lim)
                    {
                        p.Frame--;
                        return;
                    }
                }
                else
                {
                    int lim = (fl & 0x38) != 0 ? 3 : 6;
                    if (++p.Frame < lim)
                    {
                        return;
                    }
                }
                p.StateFlags &= ~PlayerState.FlagTurning;
                p.Frame = (fl & 0x20) != 0 ? 11 : (fl & 0x40) != 0 ? 12 : 0;
            }
            else if ((fl & PlayerState.FlagDying) != 0)
            {
                int last = p.Form == PlayerState.Walker ? 23 : 11;
                if (++p.Frame > last)
                {
                    p.StateFlags &= ~PlayerState.FlagDying;
                    p.PlayState = 1;
                }
            }
            else if ((fl & PlayerState.FlagTransportOut) != 0 && (fl & PlayerState.FlagTransportIn) == 0)
            {
                if (++p.Frame > 11)
                {
                    p.StateFlags = (p.StateFlags & ~PlayerState.FlagTransportOut) | PlayerState.FlagTransportIn;
                    p.Frame = 11;
                    EraseMarks(p.X, p.Y, FormW[p.Form], FormH[p.Form]);
                    p.X = transportDestX;
                    p.Y = transportDestY;
                    Screen.FillRect(0, 0, ViewWidthPx, ViewHeightPx, 255); // White flash
                    SceneAdd(new SceneOp(SceneOpKind.Flash, 0, 0, null, 0, 0, 0, 0, 0, null));
                    if (!NetActive)
                    {
                        StampMarks(p.X, p.Y, FormW[p.Form], FormH[p.Form]); // Network play: PlayerAnimate does it next frame
                    }
                }
            }
            else if ((fl & PlayerState.FlagTransportIn) != 0)
            {
                if (p.Frame > 0)
                {
                    p.Frame--;
                    return;
                }
                p.StateFlags &= ~PlayerState.FlagTransportIn;
                if (TurnSprite(p.Form) != null)
                {
                    p.StateFlags |= PlayerState.FlagTurning;
                    if (p.Facing != 1)
                    {
                        p.Facing = 1;
                        p.Frame = 2;
                    }
                    else
                    {
                        p.Facing = 2;
                        p.Frame = 3;
                    }
                }
            }
            else if ((fl & PlayerState.FlagGoingHome) != 0)
            {
                if (++p.Frame > 17)
                {
                    p.PlayState = 4;
                    PlayMovie("2Home.avi");
                }
            }
        }

        /// <summary>Pixel-exact test against the frame drawn last; world pixel coordinates.</summary>
        public bool PlayerPixelHit(int x, int y)
        {
            if (NetActive)
            {
                return true; // Network play: a box test
            }
            int dx = x - S * Player.X;
            int dy = y - S * Player.Y;
            if (dx <= 0 || dy <= 0 || drawnStrip == null)
            {
                return false;
            }
            var set = PlayerSprites[Math.Clamp(Player.SpriteSet, 0, PlayerSprites.Length - 1)];
            if (dx >= set.W * S || dy >= set.H * S)
            {
                return false;
            }
            int sx = drawnFrameX + dx, sy = drawnFrameY + dy;
            return sx < drawnStrip.Width && sy < drawnStrip.Height && drawnStrip[sx, sy] != 0;
        }

        /// <summary><paramref name="raw"/> (hazard tiles) is not divided by armour.</summary>
        public void DamagePlayer(int damage, bool raw)
        {
            var p = Player;
            uint fl = p.StateFlags;
            if (!PhysicsOn)
            {
                return;
            }
            if (damage == 0 || p.Energy == 0 || Inv.IsActive(ItemType.MegaArmor) || (p.StateFlags & 0x30) != 0)
            {
                // No damage.
                if (p.Energy != 0 || p.PlayState != 0 || (p.StateFlags & PlayerState.FlagDying) != 0)
                {
                    return;
                }
                p.StateFlags |= PlayerState.FlagDying;
                if (TurnSprite(p.Form) != null)
                {
                    p.StateFlags = fl | 0xc;
                    if (p.Facing != 1)
                    {
                        p.Frame = 5;
                        p.Facing = 1;
                    }
                    else
                    {
                        p.Facing = 2;
                        p.Frame = 0;
                    }
                }
                return;
            }
            int divisor = FormDamageDivisor[p.Form];
            if (Inv.IsActive(ItemType.ShotArmor))
            {
                divisor = ShotArmorDivisor;
            }
            if (!raw)
            {
                damage /= divisor;
            }
            if (damage == 0)
            {
                return;
            }
            p.Energy = p.Energy > damage ? p.Energy - damage : 0;
            p.HitFlash = 1;
            if (p.Energy != 0)
            {
                Sounds.PlayChannel(p.Energy >= 61 ? 0x4a : p.Energy >= 31 ? 0x4b : 0x4c, 1);
                p.StateFlags |= PlayerState.FlagFlinch;
                return;
            }
            if (p.PlayState != 0)
            {
                p.HitFlash = 1;
                return;
            }
            Sounds.Play(Random.Next() % 2 == 0 ? 0x1b : 0x20); // FAILMISN / MISNFAIL
            if (NetActive)
            {
                p.Score = p.Score >= 25 ? p.Score - 25 : 0; // Network play: a death costs 25 points
            }
            p.StateFlags |= PlayerState.FlagDying;
            if (TurnSprite(p.Form) != null)
            {
                p.StateFlags = fl | 0xc; // Turn to face the camera first
                if (p.Facing != 1)
                {
                    p.Facing = 1;
                    p.Frame = 5;
                }
                else
                {
                    p.Facing = 2;
                    p.Frame = 0;
                }
            }
            else
            {
                p.Frame = 0;
            }
        }

        private static bool IsLava(int t) => (t > 0x48 && t < 0x4c) || (t > 0x4e && t < 0x52) || (t > 0x54 && t < 0x58);

        private static bool IsAcid(int t) => (t > 0x4b && t < 0x4f) || (t > 0x51 && t < 0x55) || (t > 0x57 && t < 0x5b);

        /// <summary>Lava/acid, enemy contact, tank digging, the exit.</summary>
        private void PlayerHazards()
        {
            if (!LevelGfxReady)
            {
                return;
            }
            var p = Player;
            int w = FormW[p.Form], h = FormH[p.Form];

            // Lava and acid tiles, every 4th frame.
            if (FrameCounter % 4 == 0)
            {
                int c0 = 0, cols = w, rows = h;
                if (p.Form == PlayerState.Walker)
                {
                    c0 = 1;
                    cols = w - 2;
                    rows = h + 1;
                }
                else if (p.Form == PlayerState.Tank)
                {
                    rows = h + 1;
                }
                bool done = false;
                for (int r = 0; r < rows && !done; r++)
                {
                    for (int c = c0; c < c0 + cols; c++)
                    {
                        int idx = (p.Y + r) * MapW + p.X + c;
                        if (idx > Map.Count)
                        {
                            done = true;
                            break;
                        }
                        int t = idx < Map.Count ? Map.Tile(idx) : 0;
                        if (IsLava(t) && !Inv.IsActive(ItemType.LavaArmor) && !Inv.IsActive(ItemType.MegaArmor))
                        {
                            DamagePlayer(2, true);
                            Sounds.Play(0x14);
                            done = true;
                            break;
                        }
                        if (IsAcid(t) && !Inv.IsActive(ItemType.AcidArmor) && !Inv.IsActive(ItemType.MegaArmor))
                        {
                            DamagePlayer(5, true);
                            Sounds.Play(0x13);
                            done = true;
                            break;
                        }
                    }
                }
            }

            EnemyContact(p, w, h);

            // The tank digs through shoot-away cells in front of it.
            if (p.Form == PlayerState.Tank && PhysicsOn)
            {
                int col = p.Facing == 1 ? Math.Max(p.X - 1, 0) : Math.Min(p.X + FormW[PlayerState.Tank], MapW);
                bool dug = false;
                for (int r = 0; r < FormH[PlayerState.Tank]; r++)
                {
                    int idx = (p.Y + r) * MapW + col;
                    if (idx < Map.Count && Map.Tile(idx) != 0 && (Map.B2(idx) & 0x80) != 0)
                    {
                        Map.SetTile(idx, 0);
                        Map.SetB2(idx, (byte)(Map.B2(idx) & 0x7f));
                        dug = true;
                    }
                }
                if (dug)
                {
                    Sounds.PlayAt(Random.Next() % 2 == 0 ? 0 : 0x3e, 0, -850); // BASH3 / DRILL2
                }
            }

            // The exit zone.
            if (Header.ExitOpen != 0 && !NetActive)
            {
                var player = new Engine.Rect(p.X, p.Y, w, h);
                var exit = new Engine.Rect(Header.ExitX, Header.ExitY, 6, 6);
                if (!player.Intersect(exit).IsEmpty)
                {
                    p.PlayState = 2;
                }
            }
        }

        /// <summary>Enemy contact: the ring of cells around the box; only the first enemy found counts.</summary>
        private void EnemyContact(PlayerState p, int w, int h)
        {
            int ry = Math.Max(p.Y - 1, 0), rx = Math.Max(p.X - 1, 0);
            for (int r = 0; r < h + 2; r++)
            {
                for (int c = 0; c < w + 2; c++)
                {
                    int idx = (ry + r) * MapW + rx + c;
                    if (idx > Map.Count)
                    {
                        return;
                    }
                    if (idx >= Map.Count)
                    {
                        continue;
                    }
                    int id = Map.B2(idx) & 0x7f;
                    if (id == 0 || id >= Enemies.Length || Enemies[id].State != EnemyRecord.Alive)
                    {
                        continue;
                    }
                    var enemy = Enemies[id];
                    if (enemy.Type == 0x30 && p.Form == PlayerState.Walker && (c < 1 || c > 4))
                    {
                        return;
                    }
                    if (EnemyContactHurts(enemy))
                    {
                        DamagePlayer(TypeOf(enemy)?.TouchDmg ?? 0, false);
                    }
                    return;
                }
            }
        }

        /// <summary>Collects objects whose cell the player's box covers.</summary>
        private void PlayerPickups()
        {
            if (!LevelGfxReady)
            {
                return;
            }
            var p = Player;
            for (int r = 0; r < FormH[p.Form]; r++)
            {
                for (int c = 0; c < FormW[p.Form]; c++)
                {
                    int idx = (p.Y + r) * MapW + p.X + c;
                    if (idx >= Map.Count || (Map.B3(idx) & 0x3f) == 0)
                    {
                        continue;
                    }
                    Collect(idx);
                    int t = Map.B3(idx) & 0x3f;
                    if (t != 0x1a && t != 0x13)
                    {
                        Map.SetB3(idx, (byte)(Map.B3(idx) & 0xc0));
                        MapPlot(p.X + c, p.Y + r, 2, 2, 0);
                        Sounds.PlayAt(10, 0, -600);
                        int o = FindLevelObject(p.X + c, p.Y + r);
                        if (o >= 0)
                        {
                            if (NetActive && o < 100)
                            {
                                NetObjectTaken(o);
                            }
                            Objects[o].Kind = 0;
                        }
                    }
                }
            }
        }

        /// <summary>Collect: the effect of picking up object type b3 &amp; 0x3F with quantity b1 &amp; 0x7F.</summary>
        private void Collect(int idx)
        {
            var p = Player;
            int t = Map.B3(idx) & 0x3f;
            int q = Map.B1(idx) & 0x7f;
            void KeepRest(int rest) => Map.SetB1(idx, (byte)((Map.B1(idx) & 0x80) | (rest & 0x7f)));
            switch (t)
            {
                case >= 1 and <= 8:
                {
                    int total = Inv.Count[t] + q;
                    if (total > 999)
                    {
                        Inv.Count[t] = 999;
                        KeepRest(total - 999);
                    }
                    else
                    {
                        Inv.Count[t] = total;
                        KeepRest(0);
                    }
                    Sounds.Play(t switch { 1 => 0xa5, 2 => 0xa7, 3 => 0xa8, 4 => 0xa9, 5 => 0xaa, 6 => 0xab, 7 => 0xac, _ => 0xa6 });
                    break;
                }
                case 0x11 or 0x14 or 0x17 or 0x18 or 0x19 or (>= 0x1b and <= 0x20):
                    Inv.Owned[t] = 1;
                    Inv.Count[t] += 1;
                    KeepRest(0);
                    Sounds.Play(t switch
                    {
                        0x11 => 0xb1,
                        0x14 => 0xb0,
                        0x17 or 0x18 or 0x19 => 0xb2,
                        0x1b => 0xbd,
                        0x1c => 0xbc,
                        0x1d => 0xb7,
                        0x1e => 0xb6,
                        0x1f => 0xb9,
                        _ => 0xba,
                    });
                    break;
                case 0x12:
                    Inv.Count[ItemType.EnergyCell] += q;
                    KeepRest(0);
                    Sounds.Play(0xcf);
                    Sounds.Play(0xad);
                    break;
                case 0x13:
                    Sounds.Play(0xb4); // Transporter pad: FloorEffects handles it
                    break;
                case 0x15:
                {
                    int room = Math.Max(0, p.MaxEnergy - p.Energy);
                    int add = Math.Min(q, room);
                    p.Energy += add;
                    KeepRest(q - add);
                    Sounds.Play(0x7c);
                    Sounds.Play(0xae);
                    break;
                }
                case 0x16:
                {
                    int raise = Math.Min(q, 220 - p.MaxEnergy);
                    if (raise < 0)
                    {
                        raise = 0;
                    }
                    p.MaxEnergy += raise;
                    p.Energy = Math.Min(p.Energy + q, p.MaxEnergy);
                    KeepRest(q - raise);
                    Sounds.Play(0xce);
                    Sounds.Play(0xaf);
                    break;
                }
                case 0x1a:
                    if (ActivatePlatforms())
                    {
                        Sounds.Play(0x45);
                    }
                    Sounds.Play(0xb3);
                    break;
            }
        }

        /// <summary>The platform switch (and sol-switcheroo).</summary>
        private bool ActivatePlatforms()
        {
            bool any = false;
            foreach (var pl in Platforms)
            {
                if (pl.State != 0 && pl.Attacking != 0 && (pl.Flags & EnemyRecord.PlatformSwitchedOn) == 0)
                {
                    pl.Flags |= EnemyRecord.PlatformSwitchedOn;
                    any = true;
                }
            }
            return any;
        }
    }
}
