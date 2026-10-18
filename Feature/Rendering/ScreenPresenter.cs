using System.Windows;
using Rect = Thexder.Core.Engine.Rect;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Thexder.Core.Assets;
using Thexder.Core.Engine;

namespace Thexder.Feature.Rendering
{
    /// <summary>
    /// Converts a rectangle of an indexed <see cref="FrameBuffer"/> into a Bgra32 WPF bitmap through the current palette
    /// (so palette cycling shows without redrawing the game).
    /// </summary>
    internal sealed class ScreenPresenter
    {
        private WriteableBitmap? bitmap;

        public WriteableBitmap? Bitmap => bitmap;

        /// <summary>Converts the region; returns true when a new bitmap object was created (size changed).</summary>
        public unsafe bool Present(FrameBuffer frame, Rect region, Palette palette)
        {
            bool created = false;
            int w = Math.Max(1, region.Width), h = Math.Max(1, region.Height);
            if (bitmap == null || bitmap.PixelWidth != w || bitmap.PixelHeight != h)
            {
                bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                created = true;
            }

            bitmap.Lock();
            try
            {
                uint* dst = (uint*)bitmap.BackBuffer;
                int stride = bitmap.BackBufferStride / 4;
                uint[] colors = palette.Colors;
                byte[] src = frame.Pixels;
                int rows = Math.Min(h, frame.Height - region.Y);
                int cols = Math.Min(w, frame.Width - region.X);
                for (int y = 0; y < rows; y++)
                {
                    uint* row = dst + y * stride;
                    int s = (region.Y + y) * frame.Width + region.X;
                    for (int x = 0; x < cols; x++)
                    {
                        row[x] = colors[src[s + x]];
                    }
                }
                bitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
            }
            finally
            {
                bitmap.Unlock();
            }
            return created;
        }

        public bool Present(FrameBuffer frame, Palette palette) => Present(frame, new Rect(0, 0, frame.Width, frame.Height), palette);

        /// <summary>Copies a BGRA32 picture (a movie frame).</summary>
        public bool PresentBgra(byte[] pixels, int width, int height)
        {
            bool created = false;
            if (bitmap == null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
            {
                bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                created = true;
            }
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            return created;
        }
    }
}
