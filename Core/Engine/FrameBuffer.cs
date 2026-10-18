using Thexder.Core.Assets;

namespace Thexder.Core.Engine
{
    /// <summary>Sprites are drawn with palette index 0 transparent unless a different key is given.</summary>
    internal sealed class FrameBuffer
    {
        public FrameBuffer(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height];
            Clip = new Rect(0, 0, width, height);
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] Pixels { get; }

        /// <summary>Drawing outside this rectangle is discarded.</summary>
        public Rect Clip { get; set; }

        public void Clear(byte color = 0) => Array.Fill(Pixels, color);

        public void FillRect(int x, int y, int width, int height, byte color)
        {
            var r = Clip.Intersect(new Rect(x, y, width, height));
            for (int row = r.Y; row < r.Bottom; row++)
            {
                Pixels.AsSpan(row * Width + r.X, r.Width).Fill(color);
            }
        }

        /// <summary>Copies a rectangle of <paramref name="source"/> to (x, y), opaque.</summary>
        public void Blit(IndexedImage source, int sx, int sy, int width, int height, int x, int y)
        {
            ClipBlit(ref sx, ref sy, ref width, ref height, ref x, ref y, source);
            for (int row = 0; row < height; row++)
            {
                source.Pixels.AsSpan((sy + row) * source.Width + sx, width).CopyTo(Pixels.AsSpan((y + row) * Width + x, width));
            }
        }

        /// <summary>
        /// Copies a rectangle of <paramref name="source"/> to (x, y), skipping pixels equal to <paramref name="key"/>.
        /// </summary>
        public void BlitTransparent(IndexedImage source, int sx, int sy, int width, int height, int x, int y, byte key = 0)
        {
            ClipBlit(ref sx, ref sy, ref width, ref height, ref x, ref y, source);
            var src = source.Pixels;
            var dst = Pixels;
            for (int row = 0; row < height; row++)
            {
                int s = (sy + row) * source.Width + sx;
                int d = (y + row) * Width + x;
                for (int i = 0; i < width; i++)
                {
                    byte p = src[s + i];
                    if (p != key)
                    {
                        dst[d + i] = p;
                    }
                }
            }
        }

        /// <summary>
        /// Draws every non-key pixel of the source rectangle in one colour (used for flashes and silhouettes).
        /// </summary>
        public void BlitSolid(IndexedImage source, int sx, int sy, int width, int height, int x, int y, byte color, byte key = 0)
        {
            ClipBlit(ref sx, ref sy, ref width, ref height, ref x, ref y, source);
            for (int row = 0; row < height; row++)
            {
                int s = (sy + row) * source.Width + sx;
                int d = (y + row) * Width + x;
                for (int i = 0; i < width; i++)
                {
                    if (source.Pixels[s + i] != key)
                    {
                        Pixels[d + i] = color;
                    }
                }
            }
        }

        /// <summary>
        /// Draws frame <paramref name="frame"/> of a strip of equally sized frames laid out in rows (perRow = width /
        /// frameWidth), with palette index 0 transparent.
        /// </summary>
        public void BlitFrame(IndexedImage strip, int x, int y, int frameWidth, int frameHeight, int frame)
        {
            var (sx, sy) = FrameOrigin(strip, frameWidth, frameHeight, frame);
            BlitTransparent(strip, sx, sy, frameWidth, frameHeight, x, y);
        }

        /// <summary>
        /// Draws a frame of a sprite strip through a 256-entry colour remap table (identity or a tint); source index 0 is
        /// transparent.
        /// </summary>
        public Rect BlitFrameRemap(IndexedImage strip, int x, int y, int frameWidth, int frameHeight, int frame, byte[] remap)
        {
            var (sx, sy) = FrameOrigin(strip, frameWidth, frameHeight, frame);
            int width = frameWidth, height = frameHeight;
            ClipBlit(ref sx, ref sy, ref width, ref height, ref x, ref y, strip);
            var src = strip.Pixels;
            for (int row = 0; row < height; row++)
            {
                int s = (sy + row) * strip.Width + sx;
                int d = (y + row) * Width + x;
                for (int i = 0; i < width; i++)
                {
                    byte p = src[s + i];
                    if (p != 0)
                    {
                        Pixels[d + i] = remap[p];
                    }
                }
            }
            return new Rect(x, y, width, height);
        }

        /// <summary>Top-left pixel of a frame in a strip.</summary>
        public static (int X, int Y) FrameOrigin(IndexedImage strip, int frameWidth, int frameHeight, int frame)
        {
            int perRow = Math.Max(1, strip.Width / Math.Max(1, frameWidth));
            return (frame % perRow * frameWidth, frame / perRow * frameHeight);
        }

        public void DrawImage(IndexedImage image, int x, int y, bool transparent = true)
        {
            if (transparent)
            {
                BlitTransparent(image, 0, 0, image.Width, image.Height, x, y);
            }
            else
            {
                Blit(image, 0, 0, image.Width, image.Height, x, y);
            }
        }

        private void ClipBlit(ref int sx, ref int sy, ref int width, ref int height, ref int x, ref int y, IndexedImage source)
        {
            // Keep the source rectangle inside the source image.
            if (sx < 0) { width += sx; x -= sx; sx = 0; }
            if (sy < 0) { height += sy; y -= sy; sy = 0; }
            width = Math.Min(width, source.Width - sx);
            height = Math.Min(height, source.Height - sy);

            var clip = Clip;
            if (x < clip.X) { int d = clip.X - x; sx += d; width -= d; x = clip.X; }
            if (y < clip.Y) { int d = clip.Y - y; sy += d; height -= d; y = clip.Y; }
            if (x + width > clip.Right) { width = clip.Right - x; }
            if (y + height > clip.Bottom) { height = clip.Bottom - y; }
            if (width < 0) { width = 0; }
            if (height < 0) { height = 0; }
        }
    }

    internal readonly record struct Rect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;

        public int Bottom => Y + Height;

        public bool IsEmpty => Width <= 0 || Height <= 0;

        public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

        public bool Intersects(Rect other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

        public Rect Intersect(Rect other)
        {
            int x = Math.Max(X, other.X);
            int y = Math.Max(Y, other.Y);
            int right = Math.Min(Right, other.Right);
            int bottom = Math.Min(Bottom, other.Bottom);
            return right <= x || bottom <= y ? default : new Rect(x, y, right - x, bottom - y);
        }
    }
}
