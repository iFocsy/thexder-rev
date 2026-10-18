using System.Buffers.Binary;

namespace Thexder.Core.Game
{
    /// <summary>The 0x62-byte level header, kept as raw bytes with named fields.</summary>
    internal sealed class LevelHeader
    {
        public const int Size = 0x62;

        public LevelHeader()
        {
            Data = new byte[Size];
        }

        public byte[] Data { get; }

        public int Version { get => Word(0x00); set => SetWord(0x00, value); }

        public int StartX { get => Word(0x06); set => SetWord(0x06, value); }

        public int StartY { get => Word(0x08); set => SetWord(0x08, value); }

        public int ExitX { get => Word(0x0a); set => SetWord(0x0a, value); }

        public int ExitY { get => Word(0x0c); set => SetWord(0x0c, value); }

        public int TileSheet { get => Word(0x0e); set => SetWord(0x0e, value); }

        public int Background { get => Word(0x10); set => SetWord(0x10, value); }

        public int Flipped { get => Word(0x30); set => SetWord(0x30, value); }

        public int SavedFlag { get => Word(0x34); set => SetWord(0x34, value); }

        public int World { get => Word(0x38); set => SetWord(0x38, value); }

        public int Level { get => Word(0x3a); set => SetWord(0x3a, value); }

        public int Par { get => BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(0x3c)); set => BinaryPrimitives.WriteInt32LittleEndian(Data.AsSpan(0x3c), value); }

        public int PointsPerHit { get => Word(0x40); set => SetWord(0x40, value); }

        public int ExitOpen { get => Word(0x42); set => SetWord(0x42, value); }

        public int BossCount { get => Word(0x46); set => SetWord(0x46, value); }

        public int Word(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(offset));

        public void SetWord(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(Data.AsSpan(offset), (ushort)value);

        public void Load(ReadOnlySpan<byte> data) => data[..Size].CopyTo(Data);
    }
}
