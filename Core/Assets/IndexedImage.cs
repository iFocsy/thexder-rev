using System.Buffers.Binary;

namespace Thexder.Core.Assets
{
    /// <summary>An 8-bit paletted image, rows top-down, one byte per pixel and no row padding.</summary>
    internal sealed class IndexedImage
    {
        public IndexedImage(int width, int height, byte[] pixels, Palette? palette = null)
        {
            if (pixels.Length < width * height)
            {
                throw new ArgumentException("Pixel buffer is smaller than the image.", nameof(pixels));
            }
            Width = width;
            Height = height;
            Pixels = pixels;
            Palette = palette;
        }

        public IndexedImage(int width, int height)
            : this(width, height, new byte[width * height])
        {
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] Pixels { get; }

        /// <summary>The colour table stored with the bitmap, if it came from a DIB.</summary>
        public Palette? Palette { get; }

        public byte this[int x, int y] => Pixels[y * Width + x];

        /// <summary>
        /// Decodes an uncompressed 8-bit DIB (BITMAPINFOHEADER + colour table + bits) as stored in RT_BITMAP resources
        /// and .BMP files (after the 14-byte file header).
        /// </summary>
        public static IndexedImage FromDib(ReadOnlySpan<byte> dib, bool storedTopDown = true)
        {
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
            int width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
            int height = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
            int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
            int compression = BinaryPrimitives.ReadInt32LittleEndian(dib[16..]);
            int colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
            if (bitCount != 8 || compression != 0)
            {
                throw new InvalidDataException($"Only uncompressed 8-bit bitmaps are supported (got {bitCount} bpp, compression {compression}).");
            }
            if (colorsUsed == 0)
            {
                colorsUsed = 256;
            }

            var palette = Palette.FromRgbQuads(dib.Slice(headerSize, colorsUsed * 4));
            bool topDown = height < 0 || storedTopDown;
            height = Math.Abs(height);
            int stride = (width + 3) & ~3;
            var bits = dib[(headerSize + colorsUsed * 4)..];
            if (bits.Length < stride * (height - 1) + width)
            {
                throw new InvalidDataException("Bitmap data is truncated.");
            }

            byte[] pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                int source = (topDown ? y : height - 1 - y) * stride;
                bits.Slice(source, width).CopyTo(pixels.AsSpan(y * width, width));
            }
            return new IndexedImage(width, height, pixels, palette);
        }

        /// <summary>Copies a rectangle into a new image (used to cut frames out of sprite strips).</summary>
        public IndexedImage Crop(int x, int y, int width, int height)
        {
            byte[] pixels = new byte[width * height];
            for (int row = 0; row < height; row++)
            {
                Pixels.AsSpan((y + row) * Width + x, width).CopyTo(pixels.AsSpan(row * width, width));
            }
            return new IndexedImage(width, height, pixels, Palette);
        }
    }
}
