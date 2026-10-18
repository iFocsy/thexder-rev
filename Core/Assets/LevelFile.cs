using System.Buffers.Binary;

namespace Thexder.Core.Assets
{
    /// <summary>Interpretation of the blocks lives with the game code that uses them.</summary>
    internal sealed class LevelFile
    {
        public const int Version = 3;
        public const int HeaderSize = 0x62;
        public const int MapWidth = 320;
        public const int MapHeight = 68;
        public const int CellSize = 4;
        public const int EnemyCount = 100;
        public const int EnemyRecordSize = 50;
        public const int ThexderRecordSize = 0x42;
        public const int PlatformCount = 30;
        public const int PlatformRecordSize = 50;
        public const int ObjectCount = 100;
        public const int ObjectRecordSize = 18;
        public const int FileSize = HeaderSize + 4 + MapWidth * MapHeight * CellSize + 2 + EnemyCount * EnemyRecordSize
            + ThexderRecordSize + PlatformCount * PlatformRecordSize + ObjectCount * ObjectRecordSize;

        private LevelFile(byte[] header, byte[] cells, byte[] enemies, byte[] thexder, byte[] platforms, byte[] objects)
        {
            Header = header;
            Cells = cells;
            Enemies = enemies;
            Thexder = thexder;
            Platforms = platforms;
            Objects = objects;
        }

        public byte[] Header { get; }

        /// <summary>Row-major 320 x 68 cells of 4 bytes.</summary>
        public byte[] Cells { get; }

        public byte[] Enemies { get; }

        public byte[] Thexder { get; }

        public byte[] Platforms { get; }

        public byte[] Objects { get; }

        public short HeaderWord(int index) => BinaryPrimitives.ReadInt16LittleEndian(Header.AsSpan(index * 2));

        public static string FileName(int world, int level) => $"W{world:00}L{level:00}.THL";

        public static LevelFile Load(string path) => Parse(File.ReadAllBytes(path), Path.GetFileName(path));

        public static LevelFile Parse(byte[] data, string name = "level")
        {
            if (data.Length < FileSize - ObjectCount * ObjectRecordSize - PlatformCount * PlatformRecordSize)
            {
                throw new InvalidDataException($"{name} is too short to be a Thexder level ({data.Length} bytes).");
            }
            if (BinaryPrimitives.ReadInt16LittleEndian(data) != Version)
            {
                throw new InvalidDataException($"{name} is not a valid Thexder level (version {BinaryPrimitives.ReadInt16LittleEndian(data)}).");
            }
            int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(HeaderSize));
            int height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(HeaderSize + 2));
            if (width != MapWidth || height != MapHeight)
            {
                throw new InvalidDataException($"{name} has an unexpected map size {width}x{height}.");
            }

            int pos = 0;
            byte[] Take(int count, bool optional = false)
            {
                byte[] block = new byte[count];
                int available = Math.Max(0, Math.Min(count, data.Length - pos));
                if (available < count && !optional)
                {
                    throw new InvalidDataException($"{name} is truncated.");
                }
                Array.Copy(data, pos, block, 0, available);
                pos += count;
                return block;
            }

            byte[] header = Take(HeaderSize);
            pos += 4;
            byte[] cells = Take(MapWidth * MapHeight * CellSize);
            pos += 2;
            byte[] enemies = Take(EnemyCount * EnemyRecordSize);
            byte[] thexder = Take(ThexderRecordSize);
            byte[] platforms = Take(PlatformCount * PlatformRecordSize, optional: true);
            byte[] objects = Take(ObjectCount * ObjectRecordSize, optional: true);
            return new LevelFile(header, cells, enemies, thexder, platforms, objects);
        }
    }
}
