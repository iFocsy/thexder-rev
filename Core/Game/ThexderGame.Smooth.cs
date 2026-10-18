using Thexder.Core.Assets;
using Thexder.Core.Engine;

namespace Thexder.Core.Game
{
    /// <summary>
    /// Smooth motion: the game still runs at its 15 ticks a second, but the playfield is redrawn at the display's rate
    /// with the camera, the robot, the enemies, shots, falling objects and moving platforms placed between where they
    /// were on the previous tick and where they are now.
    /// </summary>
    internal sealed partial class ThexderGame
    {
        private const int EntityPlayer = 1 << 16;
        private const int EntityEnemy = 2 << 16;
        private const int EntityShot = 3 << 16;
        private const int EntityObject = 4 << 16;
        private const int EntityRemote = 5 << 16;
        private const int EntityPlatform = 6 << 16;
        private const int MaxStepCells = 8; // A longer move is a teleport: no gliding
        private const int CameraSnapCells = 12; // The eased camera jumps when it is this far behind
        private const double CameraSmoothTime = 0.09;
        private const int ObjectMarginCells = 4; // Pickups recorded around the view, for the trailing camera

        private SceneFrame sceneBuilding = new();
        private SceneFrame sceneCurrent = new();
        private SceneFrame scenePrevious = new();
        private bool sceneOpen;
        private int sceneEntity; // The thing being drawn now (0 = the world)
        private int sceneFallback; // Used when that thing did not exist on the previous tick (a shot's owner)
        private int sceneGeneration; // Changes with every level (re)start
        private int shotSerial;
        private double camEaseX, camEaseY, camVelX, camVelY;
        private bool camEaseValid;
        private int camEaseGeneration;

        /// <summary>Ease the camera (part of Smooth Motion; off in exactness tests).</summary>
        public bool SmoothCamera { get; set; } = true;

        /// <summary>Record each game frame for the smooth renderer (set by the host when Smooth Motion is on).</summary>
        public bool RecordScene { get; set; }

        /// <summary>Diagnostics: the pixel camera and the fraction of the last smooth render.</summary>
        public (int X, int Y, double Alpha) LastSmoothCamera { get; private set; }

        /// <summary>The last presented frame was a playing frame the smooth renderer can redraw.</summary>
        public bool SceneValid => RecordScene && sceneCurrent.Valid;

        private enum SceneOpKind
        {
            Sprite,
            Number,
            Laser,
            Flash,
        }

        // X, Y (and X1, Y1) are world pixels; OpCamX / OpCamY the camera the game drew it with (it can move during a
        // frame, e.g. when a transporter fires).
        private readonly record struct SceneOp(SceneOpKind Kind, int Entity, int Fallback, IndexedImage? Image, int X, int Y, int W, int H, int Frame, byte[]? Remap, int X1 = 0, int Y1 = 0, bool Alt = false, bool ZeroPad = false, int OpCamX = 0, int OpCamY = 0);

        private readonly record struct ScenePlatform(int X, int Y, int W, int H);

        private sealed class SceneFrame
        {
            public bool Valid;
            public int Generation;
            public int CamPxX, CamPxY;
            public int ForegroundCamX, ForegroundCamY;
            public readonly List<SceneOp> Ops = new();
            public readonly Dictionary<int, (int X, int Y, int Serial)> Positions = new();
            public readonly Dictionary<int, ScenePlatform> Platforms = new();
        }

        // A playing frame starts drawing.
        private void SceneBegin()
        {
            sceneOpen = RecordScene;
            if (!sceneOpen)
            {
                return;
            }
            sceneBuilding.Ops.Clear();
            sceneBuilding.CamPxX = sceneBuilding.ForegroundCamX = CamX * S;
            sceneBuilding.CamPxY = sceneBuilding.ForegroundCamY = CamY * S;
            sceneEntity = sceneFallback = 0;
        }

        private void SceneAdd(SceneOp op)
        {
            if (sceneOpen)
            {
                sceneBuilding.Ops.Add(op with { Entity = sceneEntity, Fallback = sceneFallback, OpCamX = CamX * S, OpCamY = CamY * S });
            }
        }

        private void SceneTag(int entity, int fallback = 0)
        {
            sceneEntity = entity;
            sceneFallback = fallback;
        }

        // The camera it draws with.
        private void SceneForeground()
        {
            if (sceneOpen)
            {
                sceneBuilding.ForegroundCamX = CamX * S;
                sceneBuilding.ForegroundCamY = CamY * S;
            }
        }

        private static int ShotOwnerEntity(ShotRecord s) => s.Owner == 0x7f ? EntityPlayer : s.Owner is > 0 and < EnemyCount ? EntityEnemy | s.Owner : 0;

        // Present: the frame is complete; remember where everything is.
        private void SceneEnd()
        {
            if (!sceneOpen)
            {
                sceneCurrent.Valid = false;
                return;
            }
            sceneOpen = false;
            sceneEntity = sceneFallback = 0;
            var f = sceneBuilding;
            f.Valid = true;
            f.Generation = sceneGeneration;
            f.Positions.Clear();
            f.Platforms.Clear();
            f.Positions[EntityPlayer] = (Player.X * S, Player.Y * S, 0);
            for (int i = 1; i < Enemies.Length; i++)
            {
                if (Enemies[i].State != 0)
                {
                    f.Positions[EntityEnemy | i] = (Enemies[i].X * S, Enemies[i].Y * S, 0);
                }
            }
            for (int i = 0; i < Shots.Length; i++)
            {
                if (Shots[i].State != 0)
                {
                    f.Positions[EntityShot | i] = (Shots[i].X, Shots[i].Y, Shots[i].Serial);
                }
            }
            for (int i = 0; i < Objects.Length; i++)
            {
                if (Objects[i].Kind != 0)
                {
                    f.Positions[EntityObject | i] = (Objects[i].X * S, Objects[i].Y * S, 0);
                }
            }
            for (int i = 0; i < Platforms.Length; i++)
            {
                var p = Platforms[i];
                if (p.State != 0 && p.W > 0 && p.H > 0)
                {
                    f.Positions[EntityPlatform | i] = (p.X * S, p.Y * S, 0);
                    f.Platforms[EntityPlatform | i] = new ScenePlatform(p.X, p.Y, p.W, p.H);
                }
            }
            for (int s = 0; s < NetSlots; s++)
            {
                var r = netRemotes[s];
                if (r.Id != 0)
                {
                    f.Positions[EntityRemote | s] = (r.Rec.X * S, r.Rec.Y * S, 0);
                }
            }
            (scenePrevious, sceneCurrent, sceneBuilding) = (sceneCurrent, sceneBuilding, scenePrevious);
        }

        // A frame presented outside play (banners, score screen, title...): nothing to redraw.
        private void SceneInvalidate()
        {
            sceneOpen = false;
            sceneCurrent.Valid = false;
        }

        /// <summary>
        /// Draws the playfield into <paramref name="target"/> (playfield size) as it stands <paramref name="alpha"/> of
        /// the way from the previous game frame to the last one.
        /// </summary>
        public bool RenderSmooth(double alpha, FrameBuffer target, double elapsedMs = 0)
        {
            var cur = sceneCurrent;
            if (!SceneValid || target.Width < ViewWidthPx || target.Height < ViewHeightPx)
            {
                return false;
            }
            var prev = scenePrevious;
            double back = 1 - Math.Clamp(alpha, 0, 1);
            int limit = MaxStepCells * S;
            bool continuous = prev.Valid && prev.Generation == cur.Generation
                && Math.Abs(cur.CamPxX - prev.CamPxX) <= limit && Math.Abs(cur.CamPxY - prev.CamPxY) <= limit;
            bool glide = back > 0 && continuous;
            int camX = glide ? cur.CamPxX - (int)Math.Round((cur.CamPxX - prev.CamPxX) * back) : cur.CamPxX;
            int camY = glide ? cur.CamPxY - (int)Math.Round((cur.CamPxY - prev.CamPxY) * back) : cur.CamPxY;
            if (SmoothCamera)
            {
                (camX, camY) = EaseCamera(camX, camY, continuous, cur.Generation, elapsedMs);
            }
            LastSmoothCamera = (camX, camY, alpha);

            // How far back along its last move a thing is drawn; a thing new on this tick (a shot) follows its owner.
            bool TryDelta(int entity, out (int X, int Y) offset)
            {
                offset = (0, 0);
                if (!cur.Positions.TryGetValue(entity, out var now) || !prev.Positions.TryGetValue(entity, out var then) || then.Serial != now.Serial)
                {
                    return false;
                }
                int dx = now.X - then.X, dy = now.Y - then.Y;
                if (Math.Abs(dx) <= limit && Math.Abs(dy) <= limit)
                {
                    offset = (-(int)Math.Round(dx * back), -(int)Math.Round(dy * back));
                }
                return true;
            }

            (int X, int Y) Offset(int entity, int fallback)
            {
                if (!glide || entity == 0)
                {
                    return (0, 0);
                }
                if (TryDelta(entity, out var offset))
                {
                    return offset;
                }
                return fallback != 0 && TryDelta(fallback, out var owner) ? owner : (0, 0);
            }

            var platforms = new List<(ScenePlatform Rect, int Dx, int Dy)>();
            foreach (var (key, rect) in cur.Platforms)
            {
                var (dx, dy) = Offset(key, 0);
                platforms.Add((rect, dx, dy));
            }

            target.Clip = new Rect(0, 0, ViewWidthPx, ViewHeightPx);
            DrawWorldAt(target, camX, camY, platforms);
            // How far the drawn camera trails the frame's camera; each drawing keeps the camera the game used for it.
            int driftX = cur.CamPxX - camX, driftY = cur.CamPxY - camY;
            foreach (var op in cur.Ops)
            {
                var (ox, oy) = Offset(op.Entity, op.Fallback);
                int sx = ox - op.OpCamX + driftX, sy = oy - op.OpCamY + driftY;
                switch (op.Kind)
                {
                    case SceneOpKind.Sprite when op.Alt && !SmoothCamera:
                        break;
                    case SceneOpKind.Sprite:
                        target.BlitFrameRemap(op.Image!, op.X + sx, op.Y + sy, op.W, op.H, op.Frame, op.Remap!);
                        break;
                    case SceneOpKind.Number:
                        Art.Text.DrawNumber(target, op.Frame, op.X + sx, op.Y + sy, op.W, op.Alt, op.ZeroPad, small: true);
                        break;
                    case SceneOpKind.Laser:
                        InvertTrace(target, op, sx, sy);
                        break;
                    case SceneOpKind.Flash:
                        target.FillRect(0, 0, ViewWidthPx, ViewHeightPx, 255);
                        break;
                }
            }
            DrawForegroundAt(target, cur.ForegroundCamX - driftX, cur.ForegroundCamY - driftY);
            if (ShowFps)
            {
                Art.Text.DrawNumber(target, fpsShown, 0x98, 0, 2, altColor: true, zeroPad: true, small: true);
            }
            target.Clip = new Rect(0, 0, target.Width, target.Height);
            return true;
        }

        // A critically damped spring after the target camera (Game Programming Gems 4, "Critically Damped Ease-In/
        // Ease-Out Smoothing"); it jumps on level starts, teleports and when far behind, and stays inside the map.
        private (int X, int Y) EaseCamera(int targetX, int targetY, bool continuous, int generation, double elapsedMs)
        {
            int snap = CameraSnapCells * S;
            if (!camEaseValid || !continuous || camEaseGeneration != generation
                || Math.Abs(targetX - camEaseX) > snap || Math.Abs(targetY - camEaseY) > snap)
            {
                camEaseX = targetX;
                camEaseY = targetY;
                camVelX = camVelY = 0;
                camEaseValid = true;
                camEaseGeneration = generation;
            }
            else
            {
                double dt = Math.Clamp(elapsedMs, 0, 100) / 1000.0;
                camEaseX = Spring(camEaseX, targetX, ref camVelX, dt);
                camEaseY = Spring(camEaseY, targetY, ref camVelY, dt);
            }
            int maxX = (MapW - ViewWidthCells) * S, maxY = (MapH - ViewHeightCells) * S;
            camEaseX = Math.Clamp(camEaseX, 0, maxX);
            camEaseY = Math.Clamp(camEaseY, 0, maxY);
            return ((int)Math.Round(camEaseX), (int)Math.Round(camEaseY));
        }

        private static double Spring(double current, double target, ref double velocity, double dt)
        {
            if (dt <= 0)
            {
                return current;
            }
            double omega = 2.0 / CameraSmoothTime;
            double x = omega * dt;
            double decay = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = current - target;
            double temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * decay;
            return target + (change + temp) * decay;
        }

        // The pickups just outside the view, recorded (not drawn) for the eased camera.
        private void SceneObjectsAround(int camX, int camY)
        {
            if (!sceneOpen)
            {
                return;
            }
            int m = ObjectMarginCells;
            for (int y = camY - m; y < camY + ViewHeightCells + m; y++)
            {
                for (int x = camX - m; x < camX + ViewWidthCells + m; x++)
                {
                    bool inside = x >= camX && x < camX + ViewWidthCells && y >= camY && y < camY + ViewHeightCells;
                    if (inside || x < 0 || y < 0 || x >= MapW || y >= MapH)
                    {
                        continue;
                    }
                    int type = Map.B3(y * MapW + x) & 0x3f;
                    if (type != 0 && Art.Objects != null)
                    {
                        int k = FindLevelObject(x, y);
                        SceneTag(k >= 0 ? EntityObject | k : 0);
                        SceneAdd(new SceneOp(SceneOpKind.Sprite, 0, 0, Art.Objects, x * S, y * S, 2 * S, 2 * S, type, Art.Tints.Identity, Alt: true));
                    }
                }
            }
            SceneTag(0);
        }

        // The background and the tile layer at a pixel camera; moving platforms' cells glide with their platform.
        private void DrawWorldAt(FrameBuffer target, int camPxX, int camPxY, List<(ScenePlatform Rect, int Dx, int Dy)> platforms)
        {
            int c0 = FloorDiv(camPxX, S), r0 = FloorDiv(camPxY, S);
            int bgShiftX = ScrollingBackground ? FloorDiv(Parallax * camPxX, 2 * S) : 0;
            int bgShiftY = ScrollingBackground ? FloorDiv(camPxY, S) : 0;
            bool InPlatform(int x, int y)
            {
                foreach (var (r, _, _) in platforms)
                {
                    if (x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H)
                    {
                        return true;
                    }
                }
                return false;
            }
            for (int cy = r0; cy <= r0 + ViewHeightCells; cy++)
            {
                for (int cx = c0; cx <= c0 + ViewWidthCells; cx++)
                {
                    if (cx < 0 || cy < 0 || cx >= MapW || cy >= MapH)
                    {
                        continue;
                    }
                    int tile = Map.Tile(cy * MapW + cx);
                    bool moving = platforms.Count > 0 && InPlatform(cx, cy);
                    DrawCellAt(target, moving ? 0 : tile, cx * S - camPxX, cy * S - camPxY, bgShiftX, bgShiftY, tileOnly: false);
                }
            }
            foreach (var (r, dx, dy) in platforms)
            {
                for (int cy = r.Y; cy < r.Y + r.H; cy++)
                {
                    for (int cx = r.X; cx < r.X + r.W; cx++)
                    {
                        if (cx < 0 || cy < 0 || cx >= MapW || cy >= MapH)
                        {
                            continue;
                        }
                        int tile = Map.Tile(cy * MapW + cx);
                        if (tile != 0)
                        {
                            DrawCellAt(target, tile, cx * S - camPxX + dx, cy * S - camPxY + dy, bgShiftX, bgShiftY, tileOnly: true);
                        }
                    }
                }
            }
        }

        // One cell at a screen pixel position, clipped: tile pixels over the background (DrawCell's rules), or only the
        // tile's own pixels.
        private void DrawCellAt(FrameBuffer target, int tile, int sx, int sy, int bgShiftX, int bgShiftY, bool tileOnly)
        {
            int x0 = Math.Max(0, sx), x1 = Math.Min(ViewWidthPx, sx + S);
            int y0 = Math.Max(0, sy), y1 = Math.Min(ViewHeightPx, sy + S);
            if (x0 >= x1 || y0 >= y1)
            {
                return;
            }
            var bg = Art.Background;
            var sheet = Art.TileSheet;
            byte[] dst = target.Pixels;
            byte[] bgp = bg.Pixels;
            byte[] tp = sheet.Pixels;
            int perRow = sheet.Width / S;
            bool plain = tile == 0 || tile == 0xff || tile == 0xfa;
            int tileOff = tile / perRow * sheet.Width * S + tile % perRow * S;
            int glass = tile == 0xfa ? 4 : 0;
            if (tile == 0 && S == 8 && !tileOnly)
            {
                // DrawCell's 640 path copies empty cells as aligned dwords.
                int cell = (sy + bgShiftY) * bg.Width + bgShiftX + sx;
                bgShiftX -= cell - (cell & ~3);
            }
            for (int y = y0; y < y1; y++)
            {
                int d = y * target.Width;
                int b = (y + bgShiftY) * bg.Width + bgShiftX + glass;
                int t = tileOff + (y - sy) * sheet.Width - sx;
                for (int x = x0; x < x1; x++)
                {
                    if (!plain)
                    {
                        byte p = tp[t + x];
                        if (p != 0)
                        {
                            dst[d + x] = p;
                            continue;
                        }
                    }
                    if (!tileOnly)
                    {
                        int bi = b + x;
                        dst[d + x] = (uint)bi < (uint)bgp.Length ? bgp[bi] : (byte)0;
                    }
                }
            }
        }

        // DrawForeground at a pixel camera.
        private void DrawForegroundAt(FrameBuffer target, int camPxX, int camPxY)
        {
            int c0 = FloorDiv(camPxX, S), r0 = FloorDiv(camPxY, S);
            var sheet = Art.TileSheet;
            int perRow = sheet.Width / S;
            for (int cy = r0; cy <= r0 + ViewHeightCells; cy++)
            {
                for (int cx = c0; cx <= c0 + ViewWidthCells; cx++)
                {
                    if (cx < 0 || cy < 0 || cx >= MapW || cy >= MapH)
                    {
                        continue;
                    }
                    int idx = cy * MapW + cx;
                    if ((Map.B3(idx) & 0x40) == 0)
                    {
                        continue;
                    }
                    int tile = Map.Tile(idx);
                    target.BlitTransparent(sheet, tile % perRow * S, tile / perRow * S, S, S, cx * S - camPxX, cy * S - camPxY);
                }
            }
        }

        // The laser beam again: TraceLine's own stepping from (X, Y) towards (X1, Y1), inverting pixels up to where it
        // stopped (W, H), that point included unless a hit stopped it (Frame = 1); shifted by (dx, dy) on screen.
        private void InvertTrace(FrameBuffer target, SceneOp op, int dx, int dy)
        {
            int x = op.X, y = op.Y, ex = op.X1, ey = op.Y1, stopX = op.W, stopY = op.H;
            bool hit = op.Frame != 0;
            void Invert(int px, int py)
            {
                px += dx;
                py += dy;
                if (px >= 0 && px < ViewWidthPx && py >= 0 && py < ViewHeightPx)
                {
                    int i = py * target.Width + px;
                    target.Pixels[i] = (byte)~target.Pixels[i];
                }
            }
            int ddx = ex - x, ddy = ey - y;
            int ax = 2 * Math.Abs(ddx), ay = 2 * Math.Abs(ddy);
            int sx = Math.Sign(ddx), sy = Math.Sign(ddy);
            int err = ay < ax ? -(ax >> 1) : -(ay >> 1);
            for (int n = 0; n < 8192; n++)
            {
                bool atStop = x == stopX && y == stopY;
                if (atStop && hit)
                {
                    return;
                }
                Invert(x, y);
                if (atStop)
                {
                    return;
                }
                if (ay < ax)
                {
                    err += ay;
                    if (err >= 0)
                    {
                        y += sy;
                        err -= 2 * Math.Abs(ddx);
                    }
                    x += sx;
                }
                else
                {
                    err += ax;
                    if (err >= 0)
                    {
                        x += sx;
                        err -= 2 * Math.Abs(ddy);
                    }
                    y += sy;
                }
            }
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
