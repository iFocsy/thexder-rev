using System.Buffers.Binary;

namespace Thexder.Core.Game
{
    /// <summary>An enemy ("enemy") instance, 50 bytes.</summary>
    internal sealed class EnemyRecord
    {
        public const int Size = 0x32;

        public const int Free = 0;
        public const int Alive = 1;
        public const int Dying = 2;
        public const int Remains = 3;

        public const int FlagVisible = 0x0001;
        public const int FlagFacingLeft = 0x0004;
        public const int FlagTurning = 0x0008;
        public const int FlagHopping = 0x0010;
        public const int FlagHitFlash = 0x0020;
        public const int FlagSinking = 0x0040;
        public const int FlagRising = 0x0080;
        public const int FlagSphinxTransition = 0x0100;
        public const int FlagFlinch = 0x0200;
        public const int FlagPrimusFalling = 0x0400;
        public const int BusyMask = 0x01c8;

        public const int PlatformSwitchedOn = 0x0008;

        public uint State;
        public int X;
        public int Y;
        public int W;
        public int H;
        public int Hp;
        public int Damage;
        public int Range;
        public int Type;
        public int Mode;
        public int Speed;
        public int NFrames;
        public int NDieFrames;
        public int Timer;
        public int Frame;
        public int Weapon;
        public int Dir;
        public int AnimTick;
        public int Flags;
        public int DiffMask;
        public int Attacking;
        public int AtkCooldown;
        public int Aux;
        public int AtkLatch;
        public int AtkCount;
        public int Animate;
        public int FlinchAcc;
        public int AtkIdx;

        public bool IsAlive => State == Alive;

        public void Read(ReadOnlySpan<byte> r)
        {
            State = BinaryPrimitives.ReadUInt32LittleEndian(r);
            X = U16(r, 0x04);
            Y = U16(r, 0x06);
            W = U16(r, 0x08);
            H = U16(r, 0x0a);
            Hp = U16(r, 0x0c);
            Damage = U16(r, 0x0e);
            Range = U16(r, 0x10);
            Type = U16(r, 0x12);
            Mode = U16(r, 0x14);
            Speed = U16(r, 0x16);
            NFrames = r[0x18];
            NDieFrames = r[0x19];
            Timer = U16(r, 0x1a);
            Frame = U16(r, 0x1c);
            Weapon = (short)U16(r, 0x1e);
            Dir = U16(r, 0x20);
            AnimTick = U16(r, 0x22);
            Flags = U16(r, 0x24);
            DiffMask = U16(r, 0x26);
            Attacking = r[0x28];
            AtkCooldown = r[0x29];
            Aux = U16(r, 0x2a);
            AtkLatch = r[0x2c];
            AtkCount = r[0x2d];
            Animate = r[0x2e];
            FlinchAcc = r[0x2f];
            AtkIdx = U16(r, 0x30);
        }

        public void Write(Span<byte> w)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(w, State);
            W16(w, 0x04, X);
            W16(w, 0x06, Y);
            W16(w, 0x08, W);
            W16(w, 0x0a, H);
            W16(w, 0x0c, Hp);
            W16(w, 0x0e, Damage);
            W16(w, 0x10, Range);
            W16(w, 0x12, Type);
            W16(w, 0x14, Mode);
            W16(w, 0x16, Speed);
            w[0x18] = (byte)NFrames;
            w[0x19] = (byte)NDieFrames;
            W16(w, 0x1a, Timer);
            W16(w, 0x1c, Frame);
            W16(w, 0x1e, Weapon);
            W16(w, 0x20, Dir);
            W16(w, 0x22, AnimTick);
            W16(w, 0x24, Flags);
            W16(w, 0x26, DiffMask);
            w[0x28] = (byte)Attacking;
            w[0x29] = (byte)AtkCooldown;
            W16(w, 0x2a, Aux);
            w[0x2c] = (byte)AtkLatch;
            w[0x2d] = (byte)AtkCount;
            w[0x2e] = (byte)Animate;
            w[0x2f] = (byte)FlinchAcc;
            W16(w, 0x30, AtkIdx);
        }

        public void CopyFrom(EnemyRecord o)
        {
            Span<byte> buffer = stackalloc byte[Size];
            o.Write(buffer);
            Read(buffer);
        }

        public static EnemyRecord[] ReadArray(ReadOnlySpan<byte> data, int count)
        {
            var records = new EnemyRecord[count];
            for (int i = 0; i < count; i++)
            {
                records[i] = new EnemyRecord();
                records[i].Read(data.Slice(i * Size, Size));
            }
            return records;
        }

        public static void WriteArray(EnemyRecord[] records, Span<byte> data)
        {
            for (int i = 0; i < records.Length; i++)
            {
                records[i].Write(data.Slice(i * Size, Size));
            }
        }

        private static int U16(ReadOnlySpan<byte> r, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(r[offset..]);

        private static void W16(Span<byte> w, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(w[offset..], (ushort)value);
    }
}
