using Thexder.Core.Assets;

namespace Thexder.Core.Engine
{
    /// <summary>
    /// The game's bitmap text: BigFont 16x16 and SmallFon 8x8 glyphs (A-Z, punctuation, 1-9, 0 in 13 columns) and the 8x8
    /// Numbers digits in orange and yellow.
    /// </summary>
    internal sealed class GlyphText
    {
        private const byte ScaledTransparent = 0xFD;

        public GlyphText(IndexedImage bigFont, IndexedImage smallFont, IndexedImage numbers)
        {
            BigFont = bigFont;
            SmallFont = smallFont;
            Numbers = numbers;
        }

        public IndexedImage BigFont { get; }

        public IndexedImage SmallFont { get; }

        public IndexedImage Numbers { get; }

        /// <summary>Right-aligns <paramref name="value"/> in <paramref name="digits"/> places (never truncated).</summary>
        public void DrawNumber(FrameBuffer surface, long value, int x, int y, int digits, bool altColor, bool zeroPad, bool small)
        {
            string s = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (s.Length < digits)
            {
                s = new string(zeroPad ? '0' : ' ', digits - s.Length) + s;
            }
            int gw = small ? 8 : 16;
            foreach (char c in s)
            {
                if (c != ' ')
                {
                    if (small)
                    {
                        surface.BlitFrame(Numbers, x, y, 8, 8, c - '0' + (altColor ? 10 : 0));
                    }
                    else if (c >= '0' && c <= '9')
                    {
                        surface.BlitFrame(BigFont, x, y, 16, 16, c == '0' ? 48 : c - '1' + 39);
                    }
                }
                x += gw;
            }
        }

        /// <summary>Letters only (digits and punctuation are skipped), one advance per character.</summary>
        public void DrawText(FrameBuffer surface, string text, int x, int y, bool small)
        {
            int gw = small ? 8 : 16;
            var font = small ? SmallFont : BigFont;
            foreach (char c in text)
            {
                int glyph = Glyph(c);
                if (glyph >= 0)
                {
                    surface.BlitFrame(font, x, y, gw, gw, glyph);
                }
                x += gw;
            }
        }

        /// <summary>Draws text scaled down to fit a box (item, enemy and boss names).</summary>
        public void DrawTextFit(FrameBuffer surface, string text, int x, int y, int boxWidth, int boxHeight, bool small)
        {
            if (text.Length == 0)
            {
                return;
            }
            int gw = small ? 8 : 16;
            var font = small ? SmallFont : BigFont;
            int sx = Math.Min(100, boxWidth * 100 / (gw * text.Length));
            int sy = Math.Min(100, boxHeight * 100 / gw);
            int advance = Math.Min(gw, sx * gw / 100);
            if (sx <= 0 || sy <= 0)
            {
                return;
            }
            foreach (char c in text)
            {
                int glyph = Glyph(c);
                if (glyph >= 0)
                {
                    ScaledGlyph(surface, font, x, y, gw, glyph, sx, sy);
                }
                x += advance;
            }
        }

        private static int Glyph(char c)
        {
            if (c == ' ' || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsDigit(c))
            {
                return -1;
            }
            char upper = char.ToUpperInvariant(c);
            return upper is >= 'A' and <= 'Z' ? upper - 'A' : -1;
        }

        // Nearest-neighbour scaling with percent accumulators.
        private static void ScaledGlyph(FrameBuffer surface, IndexedImage font, int x, int y, int gw, int glyph, int sx, int sy)
        {
            var (ox, oy) = FrameBuffer.FrameOrigin(font, gw, gw, glyph);
            int outH = gw * sy / 100;
            int outW = gw * sx / 100;
            int srcRows = outH * 100 / sy;
            int srcCols = outW * 100 / sx;
            int accY = 0;
            int dy = y;
            for (int r = 0; r < srcRows; r++)
            {
                accY += sy;
                if (accY <= 100)
                {
                    continue;
                }
                int rows = (accY - 1) / 100;
                accY -= rows * 100;
                for (int n = 0; n < rows; n++, dy++)
                {
                    int accX = 0;
                    int dx = x;
                    for (int c = 0; c < srcCols; c++)
                    {
                        accX += sx;
                        if (accX <= 100)
                        {
                            continue;
                        }
                        int count = (accX - 1) / 100;
                        accX -= count * 100;
                        byte p = font.Pixels[(oy + r) * font.Width + ox + c];
                        if (p != ScaledTransparent)
                        {
                            for (int k = 0; k < count; k++)
                            {
                                int px = dx + k;
                                if (px >= 0 && px < surface.Width && dy >= 0 && dy < surface.Height)
                                {
                                    surface.Pixels[dy * surface.Width + px] = p;
                                }
                            }
                        }
                        dx += count;
                    }
                }
            }
        }
    }
}
