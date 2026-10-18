namespace Thexder.Core.Game
{
    /// <summary>The classic levels are always 320 x 68 cells; generated levels can be larger.</summary>
    internal sealed class CellMap
    {
        public const int ClassicWidth = 320;
        public const int ClassicHeight = 68;
        public const int ClassicCount = ClassicWidth * ClassicHeight;
        public const int ClassicByteSize = ClassicCount * 4;

        /// <summary>Generated levels: the largest map (shots keep 16-bit pixel positions, so 2048 x 12 px fits).</summary>
        public const int MaxWidth = 2048;
        public const int MaxHeight = 272;

        public const byte PlayerMark = 0x7f;
        public const byte OccupantMask = 0x7f;
        public const byte ShootAway = 0x80;
        public const byte Seen = 0x80;
        public const byte ObjectMask = 0x3f;
        public const byte Foreground = 0x40;
        public const byte Background = 0x80;
        public const byte NonSolidMask = 0xc0;

        public CellMap()
        {
            Data = new byte[ClassicByteSize];
        }

        public byte[] Data { get; private set; }

        public int Width { get; private set; } = ClassicWidth;

        public int Height { get; private set; } = ClassicHeight;

        public int Count => Width * Height;

        public int ByteSize => Count * 4;

        public bool IsClassicSize => Width == ClassicWidth && Height == ClassicHeight;

        /// <summary>Changes the size; the cells are cleared when it changes.</summary>
        public void Resize(int width, int height)
        {
            width = Math.Clamp(width, 64, MaxWidth);
            height = Math.Clamp(height, CellMapMinHeight, MaxHeight);
            if (width == Width && height == Height)
            {
                return;
            }
            Width = width;
            Height = height;
            Data = new byte[width * height * 4];
        }

        private const int CellMapMinHeight = 34;

        public int Index(int x, int y) => y * Width + x;

        public bool InRange(int index) => index >= 0 && index < Count;

        public byte Tile(int index) => Data[index * 4];

        public void SetTile(int index, byte value) => Data[index * 4] = value;

        public byte B1(int index) => Data[index * 4 + 1];

        public void SetB1(int index, byte value) => Data[index * 4 + 1] = value;

        public byte B2(int index) => Data[index * 4 + 2];

        public void SetB2(int index, byte value) => Data[index * 4 + 2] = value;

        public byte B3(int index) => Data[index * 4 + 3];

        public void SetB3(int index, byte value) => Data[index * 4 + 3] = value;

        public byte Tile(int x, int y) => Data[Index(x, y) * 4];

        public bool IsSolid(int index) => Tile(index) != 0 && (B3(index) & NonSolidMask) == 0;

        public int Occupant(int index) => B2(index) & OccupantMask;

        public int ObjectType(int index) => B3(index) & ObjectMask;

        public void Load(ReadOnlySpan<byte> data) => data[..ByteSize].CopyTo(Data);

        /// <summary>
        /// On larger maps a negative index is simply outside the map (returned as int.MaxValue, which every caller treats
        /// as out of range).
        /// </summary>
        public int Wrap16(int index) => Count <= 0x10000 ? (ushort)index : index < 0 ? int.MaxValue : index;
    }
}
