using Thexder.Core.Assets;
using Thexder.Core.Engine;

namespace Thexder.Core.Game
{
    // Playfield and HUD drawing.
    internal sealed partial class ThexderGame
    {
        private int hudChargeDrawn = -1;
        private int hudCellsDrawn = -1;
        private int hudBossHpDrawn = -1;
        private int shakeFrames;
        private int fpsCount;
        private int fpsShown;
        private long fpsSecond;

        public bool ViewObjectsActive { get; set; }

        private Rect PlayfieldClip => new(0, 0, ViewWidthPx, ViewHeightPx);

        /// <summary>
        /// Frame <paramref name="frame"/> of a strip of wCells x hCells frames, at a map cell position, through a colour
        /// remap table (index 0 transparent), clipped to the playfield.
        /// </summary>
        public Rect DrawSpriteCells(IndexedImage? strip, int cellX, int cellY, int wCells, int hCells, int frame, byte[] remap)
        {
            if (strip == null || wCells <= 0 || hCells <= 0)
            {
                return default;
            }
            SceneAdd(new SceneOp(SceneOpKind.Sprite, 0, 0, strip, cellX * S, cellY * S, wCells * S, hCells * S, frame, remap));
            Screen.Clip = PlayfieldClip;
            var r = Screen.BlitFrameRemap(strip, (cellX - CamX) * S, (cellY - CamY) * S, wCells * S, hCells * S, frame, remap);
            Screen.Clip = new Rect(0, 0, Screen.Width, Screen.Height);
            return r;
        }

        /// <summary>As above with a world-pixel position and frame size in pixels.</summary>
        public Rect DrawSpritePixels(IndexedImage? strip, int px, int py, int wPx, int hPx, int frame, byte[] remap)
        {
            if (strip == null || wPx <= 0 || hPx <= 0)
            {
                return default;
            }
            SceneAdd(new SceneOp(SceneOpKind.Sprite, 0, 0, strip, px, py, wPx, hPx, frame, remap));
            Screen.Clip = PlayfieldClip;
            var r = Screen.BlitFrameRemap(strip, px - CamX * S, py - CamY * S, wPx, hPx, frame, remap);
            Screen.Clip = new Rect(0, 0, Screen.Width, Screen.Height);
            return r;
        }

        /// <summary>Small digits at playfield pixel (x, y), e.g. a killed enemy's points.</summary>
        public void DrawPopupNumber(int value, int x, int y, int digits, bool alt, bool zeroPad)
        {
            if (y + 8 < ViewHeightPx)
            {
                SceneAdd(new SceneOp(SceneOpKind.Number, 0, 0, null, x + CamX * S, y + CamY * S, digits, 0, value, null, Alt: alt, ZeroPad: zeroPad));
                Screen.Clip = PlayfieldClip;
                Art.Text.DrawNumber(Screen, value, x, y, digits, alt, zeroPad, small: true);
                Screen.Clip = new Rect(0, 0, Screen.Width, Screen.Height);
            }
        }

        public void MapPlot(int x, int y, int w, int h, byte color) => MapSurface.FillRect(x, y, w, h, color);

        private void MapPlotCell(int x, int y, byte color)
        {
            if (x >= 0 && x < MapW && y >= 0 && y < MapH)
            {
                MapSurface.Pixels[y * MapW + x] = color;
            }
        }

        /// <summary>Background and tile layer, map discovery, the exit door.</summary>
        private void DrawTiles()
        {
            SceneBegin();
            int camX = CamX, camY = CamY;
            for (int sy = 0; sy < ViewHeightCells; sy++)
            {
                for (int sx = 0; sx < ViewWidthCells; sx++)
                {
                    int idx = (camY + sy) * MapW + camX + sx;
                    int tile = Map.Tile(idx);
                    DrawCell(tile, sx, sy, camX, camY);
                    if (tile != 0 && (Map.B3(idx) & 0x80) == 0)
                    {
                        bool shootAwayView = (Map.B2(idx) & 0x80) != 0 && Inv.IsActive(ItemType.ViewShootAway);
                        MapPlotCell(camX + sx, camY + sy, (byte)(shootAwayView ? 0x35 : 0x45));
                        Map.SetB1(idx, (byte)(Map.B1(idx) | 0x80));
                    }
                }
            }
            if (SessionActive && Player.PlayState == 0 && Header.ExitOpen != 0 && !NetActive)
            {
                DrawSpriteCells(Art.Exit, Header.ExitX, Header.ExitY, 6, 6, 0, Art.Tints.Identity);
            }
        }

        /// <summary>The background pixel offset of a screen cell (scrolling parallax or fixed).</summary>
        private int BackgroundOffset(int sx, int sy, int camX, int camY, IndexedImage bg)
        {
            if (ScrollingBackground)
            {
                return (S * sy + camY) * bg.Width + ((Parallax * camX) >> 1) + S * sx;
            }
            return (bg.Width * sy + sx) * S;
        }

        /// <summary>Background scroll factor for this resolution and width.</summary>
        private int Parallax => (short)Math.Truncate((640 - S * ViewWidthCells) / (double)(MapW - ViewWidthCells) * 2.0);

        /// <summary>One S x S screen cell, tile over background (0 = transparent).</summary>
        private void DrawCell(int tile, int sx, int sy, int camX, int camY)
        {
            var bg = Art.Background;
            var sheet = Art.TileSheet;
            byte[] dst = Screen.Pixels;
            int stride = Screen.Width;
            int d0 = sy * S * stride + sx * S;
            int bgOff = BackgroundOffset(sx, sy, camX, camY, bg);
            if (tile == 0 && S == 8)
            {
                bgOff &= ~3; // The 640 empty-cell path copies aligned dwords
            }
            if (tile == 0xfa)
            {
                bgOff += 4; // "glass" wall: background displaced 4 px
            }
            int perRow = sheet.Width / S;
            int tileOff = tile / perRow * sheet.Width * S + tile % perRow * S;
            bool plain = tile == 0 || tile == 0xff || tile == 0xfa;
            byte[] bgp = bg.Pixels;
            byte[] tp = sheet.Pixels;
            for (int y = 0; y < S; y++)
            {
                int d = d0 + y * stride;
                int b = bgOff + y * bg.Width;
                int t = tileOff + y * sheet.Width;
                for (int x = 0; x < S; x++)
                {
                    byte bp = (uint)(b + x) < (uint)bgp.Length ? bgp[b + x] : (byte)0;
                    if (plain)
                    {
                        dst[d + x] = bp;
                    }
                    else
                    {
                        byte p = tp[t + x];
                        dst[d + x] = p != 0 ? p : bp;
                    }
                }
            }
        }

        /// <summary>Level object icons (2 x 2 cells) from cell byte 3.</summary>
        private void DrawObjects()
        {
            int camX = CamX, camY = CamY;
            for (int sy = 0; sy < ViewHeightCells; sy++)
            {
                for (int sx = 0; sx < ViewWidthCells; sx++)
                {
                    int idx = (camY + sy) * MapW + camX + sx;
                    int type = Map.B3(idx) & 0x3f;
                    if (type != 0)
                    {
                        if (sceneOpen)
                        {
                            int k = FindLevelObject(camX + sx, camY + sy);
                            SceneTag(k >= 0 ? EntityObject | k : 0); // A falling object glides
                        }
                        DrawSpriteCells(Art.Objects, camX + sx, camY + sy, 2, 2, type, Art.Tints.Identity);
                        MapPlot(camX + sx, camY + sy, 2, 2, 0x10);
                    }
                }
            }
            SceneTag(0);
            SceneObjectsAround(camX, camY);
        }

        /// <summary>Foreground cells painted again over the sprites (secret tunnels).</summary>
        private void DrawForeground()
        {
            SceneForeground();
            int camX = CamX, camY = CamY;
            var sheet = Art.TileSheet;
            int perRow = sheet.Width / S;
            for (int sy = 0; sy < ViewHeightCells; sy++)
            {
                for (int sx = 0; sx < ViewWidthCells; sx++)
                {
                    int idx = (camY + sy) * MapW + camX + sx;
                    if ((Map.B3(idx) & 0x40) == 0)
                    {
                        continue;
                    }
                    int tile = Map.Tile(idx);
                    Screen.Clip = PlayfieldClip;
                    Screen.BlitTransparent(sheet, tile % perRow * S, tile / perRow * S, S, S, sx * S, sy * S);
                    Screen.Clip = new Rect(0, 0, Screen.Width, Screen.Height);
                    if (tile != 0)
                    {
                        MapPlotCell(camX + sx, camY + sy, (byte)(Inv.IsActive(ItemType.ViewForeground) ? 0x81 : 0x45));
                        Map.SetB1(idx, (byte)(Map.B1(idx) | 0x80));
                    }
                }
            }
        }

        private void DrawFps()
        {
            long now = Clock();
            fpsCount++;
            if (now - fpsSecond >= 1000)
            {
                fpsShown = fpsCount;
                fpsCount = 0;
                fpsSecond = now;
            }
            Art.Text.DrawNumber(Screen, fpsShown, 0x98, 0, 2, altColor: true, zeroPad: true, small: true);
        }

        /// <summary>Present: screen shake for this frame, the HUD, then hand the frame to the host.</summary>
        private void Present()
        {
            int dx = 0, dy = 0;
            if (ShakeRequest != 0)
            {
                if (shakeFrames == 0)
                {
                    shakeFrames = Math.Max(2, ShakeRequest % 100);
                    ShakeRequest = Math.Clamp(ShakeRequest / 10, 2, 16);
                }
                dx = Random.Next() % ShakeRequest;
                dy = Random.Next() % ShakeRequest;
                if (--shakeFrames == 0)
                {
                    ShakeRequest = 0;
                }
            }
            ShakeOffset = (dx, dy);
            DrawHud();
            SceneEnd();
            FramePresented?.Invoke();
        }

        /// <summary>Score, charge bar, energy cells and the boss panel.</summary>
        private void DrawHud()
        {
            int h = ViewHeightPx;
            var text = Art.Text;
            Screen.Clip = new Rect(0, 0, Screen.Width, Screen.Height);
            if (!LevelGfxReady || !SessionActive)
            {
                return;
            }
            int cells = Inv.Count[ItemType.EnergyCell];
            int charge = Player.Energy;
            if (cells != hudCellsDrawn)
            {
                HudFullRedraw = true;
            }
            if (HudFullRedraw)
            {
                Screen.FillRect(0, h, Screen.Width, HudHeight, 0);
                Screen.BlitFrame(Art.Energy, 0, h, 320, 34, 0);
                Screen.Clip = new Rect(0, 0, Screen.Width, h + HudHeight);
                Screen.BlitFrame(Art.BossEnergy, 0, h + 35, 320, 34, 0);
                Screen.Clip = new Rect(0, 0, Screen.Width, Screen.Height);
            }
            if (charge < 60 && hudChargeDrawn >= 60)
            {
                Sounds.Play(0x1a);
            }
            if (charge < 30 && hudChargeDrawn >= 30)
            {
                Sounds.Play(0x19);
            }
            if (charge != hudChargeDrawn || HudFullRedraw)
            {
                DrawBar(h + 15, charge, clampThird: false);
                text.DrawNumber(Screen, charge, 0x95, h + 0x12, 3, altColor: true, zeroPad: true, small: true);
            }
            hudChargeDrawn = charge;
            text.DrawNumber(Screen, Player.Score, 7, h + 0x12, 6, altColor: false, zeroPad: true, small: true);
            if (cells != hudCellsDrawn || HudFullRedraw)
            {
                for (int k = 5; k >= cells && k >= 0; k--)
                {
                    Screen.BlitFrame(Art.UnavailableEnergyCell, 60 + 12 * k, h + 0x10, 12, 12, 0);
                }
                hudCellsDrawn = cells;
            }
            if (BossShown == 0)
            {
                if (HudFullRedraw)
                {
                    text.DrawNumber(Screen, 0, 0x95, h + 0x35, 3, altColor: true, zeroPad: true, small: true);
                    Screen.FillRect(0xbf, h + 0x32, 0x6e, 6, 0);
                }
            }
            else
            {
                var t = Enemies[BossShown];
                int hp = Math.Max(0, t.Hp - t.Damage);
                var type = TypeOf(t);
                if (type != null)
                {
                    text.DrawTextFit(Screen, type.Name, 3, h + 0x2f, 0x8a, 0x10, small: false);
                }
                if (hp != hudBossHpDrawn || HudFullRedraw)
                {
                    text.DrawNumber(Screen, hp, 0x95, h + 0x35, 3, altColor: true, zeroPad: true, small: true);
                    DrawBar(h + 0x32, Math.Min(hp, 0xdc), clampThird: true);
                    hudBossHpDrawn = hp;
                }
            }
            HudFullRedraw = false;
        }

        // Three coloured segments, 2 energy units per pixel: red to 30, yellow to 60, green above.
        private void DrawBar(int y, int v, bool clampThird)
        {
            Screen.FillRect(0xbf, y, 0x6e, 6, 0);
            Screen.FillRect(0xbf, y, Math.Min(v, 30) >> 1, 6, 0xf9);
            if (v > 30)
            {
                Screen.FillRect(0xce, y, Math.Min(v - 30, 30) >> 1, 6, 0xfb);
            }
            if (v > 60)
            {
                int third = (v - 60) >> 1;
                Screen.FillRect(0xdd, y, clampThird ? Math.Min(third, 80) : third, 6, 0xfa);
            }
        }

        /// <summary>GameFrame's banner for play states 1 (GameOver) and 2 (LevelDone / WorldDone).</summary>
        private void DrawStateBanner()
        {
            IndexedImage? picture = null;
            if (Player.PlayState == 1)
            {
                picture = Art.Sprite("GAMEOVER");
                if (!NetActive && ++deathBannerFrames == 70) // Network play: until Enter or a click
                {
                    deathBannerFrames = 0;
                    Advance();
                    return;
                }
            }
            else if (Player.PlayState == 2)
            {
                picture = Art.Sprite(Level == 5 ? "WORLDDONE" : "LEVELDONE");
            }
            if (picture != null)
            {
                Screen.FillRect(0, 0, ViewWidthPx, ViewHeightPx, 0);
                Screen.BlitFrame(picture, (ViewWidthPx - 320) >> 1, (ViewHeightPx - 272) >> 1, 320, 272, 0);
                ShakeOffset = (0, 0);
                DrawHud();
                SceneInvalidate();
                FramePresented?.Invoke();
            }
        }

        private int deathBannerFrames;

        /// <summary>The score summary picture: only the background, four labelled numbers.</summary>
        private void DrawScoreScreen(uint basic, int bonusOrPenalty, bool penalty)
        {
            for (int i = 0; i < Map.Count; i++)
            {
                Map.SetTile(i, 0);
            }
            int camX = CamX, camY = CamY;
            for (int sy = 0; sy < ViewHeightCells; sy++)
            {
                for (int sx = 0; sx < ViewWidthCells; sx++)
                {
                    DrawCell(0, sx, sy, camX, camY);
                }
            }
            int xL = ViewWidthCells == 60 ? 9 * S : S;
            int y0 = 6 * S;
            int xN = ViewWidthPx / 2 - 6;
            var text = Art.Text;
            text.DrawText(Screen, "Basic Score", xL, y0, small: false);
            text.DrawNumber(Screen, basic, xN, y0, 10, altColor: true, zeroPad: false, small: false);
            text.DrawText(Screen, penalty ? "Time Penalty" : "Time Bonus", xL, y0 + 40, small: false);
            text.DrawNumber(Screen, bonusOrPenalty, xN, y0 + 40, 10, altColor: true, zeroPad: false, small: false);
            text.DrawText(Screen, "Total Score", xL, y0 + 80, small: false);
            text.DrawNumber(Screen, Player.Score, xN, y0 + 80, 10, altColor: true, zeroPad: false, small: false);
            text.DrawText(Screen, "Par Score", xL, y0 + 120, small: false);
            text.DrawNumber(Screen, Header.Par, xN, y0 + 120, 10, altColor: true, zeroPad: false, small: false);
            ShakeOffset = (0, 0);
            DrawHud();
            SceneInvalidate();
            FramePresented?.Invoke();
        }

        /// <summary>Title screen: the current background, "Player Name" and "Click to Play".</summary>
        public void DrawTitleScreen()
        {
            Player.X = 0;
            Player.Y = 0;
            bool ready = LevelGfxReady;
            LevelGfxReady = false;
            int camX = CamX, camY = CamY;
            for (int sy = 0; sy < ViewHeightCells; sy++)
            {
                for (int sx = 0; sx < ViewWidthCells; sx++)
                {
                    DrawCell(Map.Tile(Map.Index(camX + sx, camY + sy)), sx, sy, camX, camY);
                }
            }
            LevelGfxReady = ready;
            Screen.FillRect(0, ViewHeightPx, Screen.Width, HudHeight, 0);
            Art.Text.DrawText(Screen, "Player Name", ViewWidthPx / 2 - 0x58, 0x5f, small: false);
            Art.Text.DrawText(Screen, "Click to Play", 0x38, ViewHeightPx + (int)(S * 2.5), small: false);
            ShakeOffset = (0, 0);
            SceneInvalidate();
            FramePresented?.Invoke();
        }

        /// <summary>Fills the playfield black and presents it (movie start, transitions).</summary>
        public void DrawBlack()
        {
            Screen.Clear();
            ShakeOffset = (0, 0);
            SceneInvalidate();
            FramePresented?.Invoke();
        }
    }
}
