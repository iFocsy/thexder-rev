using Thexder.Core.Assets;
using Thexder.Core.Engine;

namespace Thexder.Core.Game
{
    // Each is an indexed surface drawn with the game palette.
    internal sealed partial class ThexderGame
    {
        public const int PanelW = 144;
        public const int PanelH = 116;
        public const int EnemyPanelSize = 292;

        // Slot rectangles: 0/1 arrows or the title strip, 2..9 the eight slots (column-major).
        private static readonly Rect[] SlotRects =
        {
            R(20, 16, 64, 31), R(79, 16, 123, 31),
            R(9, 37, 38, 70), R(9, 73, 38, 106), R(41, 37, 70, 70), R(41, 73, 70, 106),
            R(73, 37, 102, 70), R(73, 73, 102, 106), R(105, 37, 134, 70), R(105, 73, 134, 106),
        };

        private bool panelColorsReady;
        private byte colorWhite, colorGreen, colorYellow, colorGold, colorYellowGreen, colorDarkGray;
        private int enemyType = -1;
        private bool enemyStats; // Statistics instead of the picture
        private int enemyAnimFrame;
        private int enemyAnimPhase;
        private long enemyAnimNext;
        private bool equipLeftLit, equipRightLit;

        public FrameBuffer WeaponsPanel { get; } = new(PanelW, PanelH);

        public FrameBuffer EquipmentPanel { get; } = new(PanelW, PanelH);

        public FrameBuffer EnemyPanel { get; } = new(EnemyPanelSize, EnemyPanelSize);

        /// <summary>The visible part of <see cref="EnemyPanel"/> (the picture is square, statistics 124 x 72).</summary>
        public Rect EnemyPanelArea { get; private set; } = new(0, 0, 1, 1);

        public IReadOnlyDictionary<int, string> ExeStrings { get; set; } = new Dictionary<int, string>();

        private static Rect R(int l, int t, int r, int b) => new(l, t, r - l, b - t);

        public string ItemName(int id) => ExeStrings.TryGetValue(309 + id, out var s) ? s : "";

        public string PlanetName(int world) => world is >= 1 and <= 10 && ExeStrings.TryGetValue(299 + world, out var s) ? s : "Thexder";

        public string DifficultyName(int d) => ExeStrings.TryGetValue(400 + d, out var s) ? s : $"Difficulty {d + 1}";

        /// <summary>"Map of Planet %s - Level %u".</summary>
        public string MapTitle => NetActive ? "Map of Planet - Network game" : $"Map of Planet {LevelLabel}";

        private void PreparePanelColors()
        {
            if (panelColorsReady)
            {
                return;
            }
            panelColorsReady = true;
            var pal = Art.BasePalette;
            colorWhite = Nearest(pal, 255, 255, 255);
            colorGreen = Nearest(pal, 19, 139, 19);
            colorYellow = Nearest(pal, 255, 255, 0);
            colorGold = Nearest(pal, 255, 227, 0);
            colorYellowGreen = Nearest(pal, 127, 187, 0);
            colorDarkGray = Nearest(pal, 64, 64, 64);
        }

        private static byte Nearest(Palette pal, int r, int g, int b)
        {
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < 256; i++)
            {
                var (pr, pg, pb) = Palette.Unpack(pal[i]);
                int d = (pr - r) * (pr - r) + (pg - g) * (pg - g) + (pb - b) * (pb - b);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return (byte)best;
        }

        private static void FrameRect(FrameBuffer s, Rect r, byte color)
        {
            s.FillRect(r.X, r.Y, r.Width, 1, color);
            s.FillRect(r.X, r.Bottom - 1, r.Width, 1, color);
            s.FillRect(r.X, r.Y, 1, r.Height, color);
            s.FillRect(r.Right - 1, r.Y, 1, r.Height, color);
        }

        /// <summary>The Weapons window.</summary>
        public void DrawWeaponsPanel()
        {
            PreparePanelColors();
            var s = WeaponsPanel;
            var text = Art.Text;
            s.Clear();
            s.BlitFrame(Art.UiBitmap2, 27, 16, 92, 16, 0);
            for (int id = WeaponListFirst + 1; id < WeaponListFirst + 9; id++)
            {
                var r = SlotRects[id - WeaponListFirst + 1];
                s.FillRect(r.X + 7, r.Y + 3, 16, 16, 0);
                if (id < Inventory.Size)
                {
                    s.BlitFrame(Art.ObjectsLowRes, r.X + 7, r.Y + 3, 16, 16, id);
                }
                int ammo = id < Inventory.Size ? Inv.Count[id] : 0;
                if (WeaponCursor == id - 1)
                {
                    text.DrawNumber(s, ammo, r.X + 3, r.Y + 0x17, 3, altColor: true, zeroPad: true, small: true);
                    FrameRect(s, r, colorWhite);
                }
                else if (AltWeaponCursor == id - 1 && Player.Form == PlayerState.Walker)
                {
                    text.DrawNumber(s, ammo, r.X + 3, r.Y + 0x17, 3, altColor: true, zeroPad: true, small: true);
                    FrameRect(s, r, colorYellow);
                }
                else
                {
                    text.DrawNumber(s, ammo, r.X + 3, r.Y + 0x17, 3, altColor: false, zeroPad: true, small: true);
                    FrameRect(s, r, colorGreen);
                }
            }
            s.FillRect(0x1e, 4, 0x69, 8, 0);
            text.DrawTextFit(s, ItemName(Player.Weapon), 0x1e, 4, 0x8c - 0x1e, 0x0c - 4, small: true);
        }

        /// <summary>The Equipment window.</summary>
        public void DrawEquipmentPanel()
        {
            PreparePanelColors();
            var s = EquipmentPanel;
            var text = Art.Text;
            s.Clear();
            s.BlitFrame(Art.UiBitmap, 20, 16, 44, 16, equipLeftLit ? 2 : 0);
            s.BlitFrame(Art.UiBitmap, 79, 16, 44, 16, equipRightLit ? 3 : 1);
            for (int n = EquipmentListFirst + 1; n < EquipmentListFirst + 9; n++)
            {
                var r = SlotRects[n - EquipmentListFirst + 1];
                s.FillRect(r.X, r.Y, r.Width, r.Height, 0);
                if (n >= 12)
                {
                    continue;
                }
                int item = ItemType.EquipmentList[n - 1];
                s.BlitFrame(Art.ObjectsLowRes, r.X + 7, r.Y + 3, 16, 16, item);
                int value;
                bool alt;
                byte brush;
                if (Inv.Owned[item] == 0 || Items[item].Timer == 0 || Inv.Active[item] == 0)
                {
                    value = Inv.Count[item];
                    if (EquipmentCursor != n - 1)
                    {
                        alt = false;
                        brush = colorGreen;
                    }
                    else
                    {
                        alt = true;
                        brush = colorWhite;
                        s.FillRect(0x1e, 4, 0x69, 8, 0);
                        text.DrawTextFit(s, ItemName(item), 0x1e, 4, 0x8c - 0x1e, 0x0c - 4, small: true);
                    }
                }
                else
                {
                    value = Inv.Active[item];
                    alt = true;
                    brush = value < Items[item].Timer / 5 ? colorGold : colorYellowGreen;
                }
                text.DrawNumber(s, value, r.X + 3, r.Y + 0x17, 3, alt, zeroPad: true, small: true);
                FrameRect(s, r, brush);
            }
        }

        /// <summary>A click in the Weapons window (surface coordinates): selects a usable weapon.</summary>
        public void ClickWeaponsPanel(int x, int y)
        {
            for (int slot = 0; slot < 8; slot++)
            {
                if (!SlotRects[slot + 2].Contains(x, y))
                {
                    continue;
                }
                int id = WeaponListFirst + slot + 1;
                if (Usable(id, Player.Form) && (Player.StateFlags & PlayerState.FlagTransforming) == 0)
                {
                    Player.Weapon = id;
                    SelectSlot(id);
                }
                else
                {
                    Sounds.Play(0x43);
                }
                Touch();
                return;
            }
        }

        /// <summary>A click in the Equipment window: scroll arrows, select, double click activates.</summary>
        public void ClickEquipmentPanel(int x, int y, bool doubleClick)
        {
            Touch();
            if (SlotRects[0].Contains(x, y))
            {
                EquipmentListFirst = Math.Max(0, EquipmentListFirst - 2);
                equipLeftLit = true;
                equipRightLit = false;
                Sounds.Play(0x42);
                return;
            }
            if (SlotRects[1].Contains(x, y))
            {
                EquipmentListFirst = Math.Min(4, EquipmentListFirst + 2);
                equipRightLit = true;
                equipLeftLit = false;
                Sounds.Play(0x42);
                return;
            }
            for (int slot = 0; slot < 8; slot++)
            {
                if (!SlotRects[slot + 2].Contains(x, y))
                {
                    continue;
                }
                int index = EquipmentListFirst + slot;
                if (index >= ItemType.EquipmentList.Length)
                {
                    return;
                }
                int item = ItemType.EquipmentList[index];
                if (Usable(item, Player.Form))
                {
                    EquipmentCursor = index;
                    Sounds.Play(0x44);
                    if (doubleClick)
                    {
                        UseEquipment(index);
                    }
                }
                else
                {
                    Sounds.Play(0x43);
                }
                return;
            }
        }

        // ---- Enemy Status --------------------------------------------------------------------------------

        /// <summary>Left click: the next enemy type, as a picture.</summary>
        public void ClickEnemyPanel(bool nextType)
        {
            Touch();
            if (!nextType)
            {
                enemyStats = true;
                return;
            }
            for (int guard = 0; guard < 64; guard++)
            {
                enemyType = (enemyType + 1) % 56;
                var t = EnemyTypes[enemyType];
                if (t != null && t.Name != "NA" && t.Name.Length > 0)
                {
                    break;
                }
            }
            enemyStats = false;
            enemyAnimPhase = 0;
            enemyAnimFrame = 0;
            enemyAnimNext = 0;
        }

        public void DrawEnemyPanel()
        {
            var s = EnemyPanel;
            s.FillRect(0, 0, EnemyPanelSize, EnemyPanelSize, 0x1f);
            if (!SessionActive || Player.PlayState != 0)
            {
                s.Clear();
                EnemyPanelArea = new Rect(0, 0, 1, 1);
                return;
            }
            if (enemyType < 0 || enemyType >= EnemyTypes.Length || EnemyTypes[enemyType] == null)
            {
                ClickEnemyPanel(nextType: true);
            }
            var t = enemyType >= 0 ? EnemyTypes[enemyType] : null;
            if (t == null)
            {
                return;
            }
            if (enemyStats)
            {
                s.Clear();
                var text = Art.Text;
                text.DrawTextFit(s, t.Name, 2, 2, 0x7c, 10, small: true);
                text.DrawText(s, "Speed", 2, 0x14, small: true);
                text.DrawNumber(s, t.Mode is 0 or 4 or 5 ? 0 : t.Speed, 0x41, 0x14, 3, false, false, true);
                text.DrawText(s, "Hits", 2, 0x1e, small: true);
                text.DrawNumber(s, t.Hp, 0x41, 0x1e, 3, false, false, true);
                text.DrawText(s, "Damage", 2, 0x28, small: true);
                if (enemyType == 6)
                {
                    text.DrawText(s, "Lots", 0x41, 0x28, small: true);
                }
                else
                {
                    text.DrawNumber(s, t.TouchDmg, 0x41, 0x28, 3, false, false, true);
                }
                if (t.Weapon >= 0)
                {
                    int best = 0;
                    foreach (var w in t.Weapons)
                    {
                        if (w.Shot >= 0)
                        {
                            best = Math.Max(best, w.Dmg);
                        }
                    }
                    text.DrawText(s, "Weapon", 2, 0x32, small: true);
                    text.DrawNumber(s, best, 0x41, 0x32, 3, false, false, true);
                }
                EnemyPanelArea = new Rect(0, 0, 0x7c, 0x48);
                return;
            }

            int wpx = t.W * S, hpx = t.H * S;
            int size = Math.Min(EnemyPanelSize, Math.Max(Math.Max(wpx, hpx), 6 * S));
            int ox = (size - wpx) / 2, oy = (size - hpx) / 2;

            // Animation: the walk strip, the turn strip twice, then the right-facing strip, then a pause.
            long now = Clock();
            if (now >= enemyAnimNext)
            {
                enemyAnimNext = now + 100;
                enemyAnimFrame++;
                int limit = enemyAnimPhase switch
                {
                    0 => Math.Max(1, t.Frames),
                    1 => 2 * t.TurnFrames,
                    2 => t.SprR != t.SprL ? Math.Max(1, t.Frames) : 0,
                    _ => 9,
                };
                if (enemyAnimFrame >= limit)
                {
                    enemyAnimFrame = 0;
                    enemyAnimPhase = (enemyAnimPhase + 1) % 4;
                }
            }
            string? strip = enemyAnimPhase switch
            {
                1 => t.SprTurn,
                2 when t.SprR != t.SprL => t.SprR,
                _ => t.SprL,
            };
            int frame = enemyAnimPhase is 0 or 1 or 2 ? enemyAnimFrame : 0;
            if (enemyAnimPhase == 1 && t.TurnFrames == 0)
            {
                strip = t.SprL;
                frame = 0;
            }
            var sprite = Art.Sprite(strip) ?? Art.Sprite(t.SprL);
            if (sprite != null)
            {
                s.Clip = new Rect(0, 0, size, size);
                s.BlitFrame(sprite, ox, oy, wpx, hpx, frame);
                s.Clip = new Rect(0, 0, EnemyPanelSize, EnemyPanelSize);
            }
            EnemyPanelArea = new Rect(0, 0, size, size);
        }
    }
}
