using Thexder.Core.Assets;
using Thexder.Core.Game;

namespace Thexder.Feature.Procedural
{
    /// <summary>
    /// A small picture of a generated level for the setup dialog: one pixel per cell, each solid tile in the average
    /// colour of its picture on the planet's tile sheet, decoration dimmed, pickups yellow, enemies red, the start green
    /// and the exit gold.
    /// </summary>
    internal static class MapPreview
    {
        private const uint Sky = 0xff141a24;
        private static readonly Dictionary<(ResourceLibrary, int), uint[]> TileColorCache = new();

        /// <summary>
        /// The average colour of every tile of tile sheet 600 + <paramref name="sheet"/> (UI thread: reads the art).
        /// </summary>
        public static uint[] TileColors(GameArt art, int sheet)
        {
            lock (TileColorCache)
            {
                if (TileColorCache.TryGetValue((art.Library, sheet), out var cached))
                {
                    return cached;
                }
                int id = art.Library.HasBitmap(600 + sheet) ? 600 + sheet : 600;
                var image = art.Library.Bitmap(id);
                uint[] palette = art.BasePalette.Colors;
                int s = art.S, perRow = image.Width / s;
                var colors = new uint[256];
                for (int t = 0; t < 256; t++)
                {
                    long r = 0, g = 0, b = 0, n = 0;
                    int ox = t % perRow * s, oy = t / perRow * s;
                    for (int y = oy; y < oy + s && y < image.Height; y++)
                    {
                        for (int x = ox; x < ox + s && x < image.Width; x++)
                        {
                            byte index = image.Pixels[y * image.Width + x];
                            if (index == 0)
                            {
                                continue;
                            }
                            uint c = palette[index];
                            r += (c >> 16) & 0xff;
                            g += (c >> 8) & 0xff;
                            b += c & 0xff;
                            n++;
                        }
                    }
                    colors[t] = n == 0 ? 0xff808080 : 0xff000000 | (uint)(r / n << 16) | (uint)(g / n << 8) | (uint)(b / n);
                }
                TileColorCache[(art.Library, sheet)] = colors;
                return colors;
            }
        }

        /// <summary>BGRA pixels, <see cref="LevelData.Width"/> x <see cref="LevelData.Height"/>.</summary>
        public static uint[] Render(LevelData level, uint[] tileColors)
        {
            int w = level.Width, h = level.Height;
            var pixels = new uint[w * h];
            byte[] cells = level.Cells;
            for (int i = 0; i < w * h; i++)
            {
                int tile = cells[i * 4];
                byte flags = cells[i * 4 + 3];
                uint c = Sky;
                if (tile != 0)
                {
                    c = (flags & 0x80) != 0 ? Blend(tileColors[tile], Sky, 0.35) : tileColors[tile];
                }
                if ((flags & 0x3f) != 0)
                {
                    c = 0xffffe14a;
                }
                pixels[i] = c;
            }
            void Box(int x, int y, int bw, int bh, uint color)
            {
                for (int cy = Math.Max(0, y); cy < Math.Min(h, y + bh); cy++)
                {
                    for (int cx = Math.Max(0, x); cx < Math.Min(w, x + bw); cx++)
                    {
                        pixels[cy * w + cx] = color;
                    }
                }
            }
            foreach (var t in level.Enemies)
            {
                Box(t.X, t.Y, Math.Max(2, Math.Min(t.W, 6)), Math.Max(2, Math.Min(t.H, 6)), 0xffe8413c);
            }
            Box(level.StartX, level.StartY, 4, 6, 0xff46e070);
            Box(level.ExitX, level.ExitY, 6, 6, 0xffffb000);
            return pixels;
        }

        private static uint Blend(uint a, uint b, double t)
        {
            uint Mix(int shift) => (uint)(((a >> shift) & 0xff) * t + ((b >> shift) & 0xff) * (1 - t)) << shift;
            return 0xff000000 | Mix(16) | Mix(8) | Mix(0);
        }
    }
}
