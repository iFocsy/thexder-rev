namespace Thexder.Core.Assets
{
    /// <summary>256 colours as 0xAARRGGBB, ready to be written into a Bgra32 bitmap.</summary>
    internal sealed class Palette
    {
        public Palette()
        {
            Colors = new uint[256];
        }

        private Palette(uint[] colors)
        {
            Colors = colors;
        }

        public uint[] Colors { get; }

        public uint this[int index]
        {
            get => Colors[index];
            set => Colors[index] = value;
        }

        public static Palette FromRgbQuads(ReadOnlySpan<byte> quads)
        {
            var colors = new uint[256];
            int count = Math.Min(256, quads.Length / 4);
            for (int i = 0; i < count; i++)
            {
                colors[i] = Pack(quads[i * 4 + 2], quads[i * 4 + 1], quads[i * 4]);
            }
            return new Palette(colors);
        }

        public static uint Pack(int r, int g, int b) => 0xff000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;

        public static (byte R, byte G, byte B) Unpack(uint color) => ((byte)(color >> 16), (byte)(color >> 8), (byte)color);

        public Palette Clone() => new((uint[])Colors.Clone());

        public void CopyFrom(Palette other) => Array.Copy(other.Colors, Colors, 256);
    }
}
