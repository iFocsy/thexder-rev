using Thexder.Core.Assets;

namespace Thexder.Core.Engine
{
    /// <summary>
    /// Colour remap tables for sprites: the identity table, the red "hit" flash and the armour colours of the player.
    /// </summary>
    internal sealed class TintTables
    {
        public TintTables(Palette palette)
        {
            Identity = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                Identity[i] = (byte)i;
            }
            Hit = Build(palette, 100, 100, 25, 25);
            Gold = Build(palette, 100, 100, 100, 0);
            LightGreen = Build(palette, 100, 50, 100, 0);
            Cyan = Build(palette, 100, 0, 100, 100);
            Magenta = Build(palette, 100, 100, 0, 100);
            // Network player colours; colour 0 is the untinted host.
            playerColours = new[]
            {
                Identity, Gold, LightGreen, Cyan, Magenta,
                Build(palette, 100, 0, 50, 100),
                Build(palette, 100, 0, 100, 50),
                Build(palette, 100, 0, 0, 100),
                Build(palette, 100, 75, 0, 100),
                Build(palette, 100, 0, 100, 0), // Dark green
            };
        }

        private readonly byte[][] playerColours;

        /// <summary>A network player's colour table by index (0 = untinted).</summary>
        public byte[] PlayerColour(int index) => index >= 0 && index < playerColours.Length ? playerColours[index] : Identity;

        public byte[] Identity { get; }

        /// <summary>Red flash of a player or enemy that was just hit.</summary>
        public byte[] Hit { get; }

        /// <summary>Mega-Armor (item 29).</summary>
        public byte[] Gold { get; }

        /// <summary>Acid Armor (item 25).</summary>
        public byte[] LightGreen { get; }

        /// <summary>Shot Armor (item 23).</summary>
        public byte[] Cyan { get; }

        /// <summary>Lava Armor (item 24).</summary>
        public byte[] Magenta { get; }

        public static byte[] Build(Palette palette, int light, int redPercent, int greenPercent, int bluePercent)
        {
            var table = new byte[256];
            var target = new (int R, int G, int B)[256];
            for (int i = 16; i < 224; i++)
            {
                var (pr, pg, pb) = Palette.Unpack(palette[i]);
                double r = Math.Min(1.0, pr * (1 / 256.0) * redPercent * 0.01);
                double g = Math.Min(1.0, pg * (1 / 256.0) * greenPercent * 0.01);
                double b = Math.Min(1.0, pb * (1 / 256.0) * bluePercent * 0.01);
                RgbToHls(r, g, b, out double h, out double s, out double l);
                l = l * light * 0.01;
                HlsToRgb(h, s, l, out double r2, out double g2, out double b2);
                target[i] = ((byte)(int)(r2 * 256.0), (byte)(int)(g2 * 256.0), (byte)(int)(b2 * 256.0));
            }
            for (int i = 16; i < 224; i++)
            {
                int best = 16, bestDistance = 0x7fff;
                for (int j = 16; j < 224; j++)
                {
                    var (pr, pg, pb) = Palette.Unpack(palette[j]);
                    int d = Math.Abs(pr - target[i].R) + Math.Abs(pg - target[i].G) + Math.Abs(pb - target[i].B);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = j;
                    }
                }
                table[i] = (byte)best;
            }
            return table;
        }

        private static void RgbToHls(double r, double g, double b, out double h, out double s, out double l)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) * 0.5;
            if (max == min)
            {
                h = 0;
                s = 0;
                return;
            }
            double d = max - min;
            s = l < 0.5 ? d / (max + min) : d / (2.0 - d);
            double rc = (max - r) / d, gc = (max - g) / d, bc = (max - b) / d;
            if (max == r)
            {
                h = bc - gc;
            }
            else if (max == g)
            {
                h = 2 + rc - bc;
            }
            else
            {
                h = 4 + gc - rc;
            }
            h *= 60;
            if (h < 0)
            {
                h += 360;
            }
        }

        private static void HlsToRgb(double h, double s, double l, out double r, out double g, out double b)
        {
            double m2 = (s + 1.0) * l;
            double m1 = 2 * l - m2;
            if (s == 0)
            {
                r = g = b = l;
                return;
            }
            r = Value(m1, m2, h + 120);
            g = Value(m1, m2, h);
            b = Value(m1, m2, h - 120);
        }

        private static double Value(double m1, double m2, double h)
        {
            if (h > 360)
            {
                h -= 360;
            }
            if (h < 0)
            {
                h += 360;
            }
            if (h < 60)
            {
                return m1 + (m2 - m1) * h * (1 / 60.0);
            }
            if (h < 180)
            {
                return m2;
            }
            if (h < 240)
            {
                return m1 + (240 - h) * (m2 - m1) * (1 / 60.0);
            }
            return m1;
        }
    }
}
