using System.Buffers.Binary;

namespace Thexder.Core.Game
{
    /// <summary>A shot instance.</summary>
    internal sealed class ShotRecord
    {
        /// <summary>Port only (not saved): changes with every shot fired in this slot, for the smooth renderer.</summary>
        public int Serial;

        public const int Size = 0x3b;
        public const int Count = 50;

        public int State;
        public int ViewBusy;
        public int NetPlayer;
        public int Reserved;
        public int Type;
        public int Owner;
        public int Damage;
        public int X0;
        public int Y0;
        public int X1;
        public int Y1;
        public int HitTarget;
        public int HitEnemy;
        public int X;
        public int Y;
        public float Yf;
        public int Dx;
        public float Dy;
        public int Speed;
        public int Frame;
        public int FaceRight;
        public int ArcCounter;
        public int ExplVariant;
        public int NetCounter;

        public void Reset()
        {
            State = 0;
            ViewBusy = 0;
            Owner = -1;
            Reserved = 0;
            Type = 1;
            HitTarget = 0;
            HitEnemy = -1;
        }

        public void Read(ReadOnlySpan<byte> r)
        {
            State = BinaryPrimitives.ReadInt32LittleEndian(r);
            ViewBusy = BinaryPrimitives.ReadInt32LittleEndian(r[0x04..]);
            NetPlayer = BinaryPrimitives.ReadInt32LittleEndian(r[0x08..]);
            Reserved = S16(r, 0x0c);
            Type = U16(r, 0x0e);
            Owner = S16(r, 0x10);
            Damage = U16(r, 0x12);
            X0 = S16(r, 0x14);
            Y0 = S16(r, 0x16);
            X1 = S16(r, 0x18);
            Y1 = S16(r, 0x1a);
            HitTarget = BinaryPrimitives.ReadInt32LittleEndian(r[0x1c..]);
            HitEnemy = S16(r, 0x20);
            X = S16(r, 0x22);
            Y = S16(r, 0x24);
            Yf = BinaryPrimitives.ReadSingleLittleEndian(r[0x26..]);
            Dx = S16(r, 0x2a);
            Dy = BinaryPrimitives.ReadSingleLittleEndian(r[0x2c..]);
            Speed = U16(r, 0x30);
            Frame = U16(r, 0x32);
            FaceRight = r[0x34];
            ArcCounter = U16(r, 0x35);
            ExplVariant = U16(r, 0x37);
            NetCounter = U16(r, 0x39);
        }

        public void Write(Span<byte> w)
        {
            BinaryPrimitives.WriteInt32LittleEndian(w, State);
            BinaryPrimitives.WriteInt32LittleEndian(w[0x04..], ViewBusy);
            BinaryPrimitives.WriteInt32LittleEndian(w[0x08..], NetPlayer);
            W16(w, 0x0c, Reserved);
            W16(w, 0x0e, Type);
            W16(w, 0x10, Owner);
            W16(w, 0x12, Damage);
            W16(w, 0x14, X0);
            W16(w, 0x16, Y0);
            W16(w, 0x18, X1);
            W16(w, 0x1a, Y1);
            BinaryPrimitives.WriteInt32LittleEndian(w[0x1c..], HitTarget);
            W16(w, 0x20, HitEnemy);
            W16(w, 0x22, X);
            W16(w, 0x24, Y);
            BinaryPrimitives.WriteSingleLittleEndian(w[0x26..], Yf);
            W16(w, 0x2a, Dx);
            BinaryPrimitives.WriteSingleLittleEndian(w[0x2c..], Dy);
            W16(w, 0x30, Speed);
            W16(w, 0x32, Frame);
            w[0x34] = (byte)FaceRight;
            W16(w, 0x35, ArcCounter);
            W16(w, 0x37, ExplVariant);
            W16(w, 0x39, NetCounter);
        }

        private static int U16(ReadOnlySpan<byte> r, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(r[offset..]);

        private static int S16(ReadOnlySpan<byte> r, int offset) => BinaryPrimitives.ReadInt16LittleEndian(r[offset..]);

        private static void W16(Span<byte> w, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(w[offset..], (ushort)value);
    }

    /// <summary>A level object.</summary>
    internal sealed class LevelObject
    {
        public const int Size = 0x12;
        public const int Count = 100;

        public const int KindUnused = 0;
        public const int KindPickup = 1;
        public const int KindBomb = 2;

        public int Kind;
        public int X;
        public int Y;
        public int Type;
        public int Amount;
        public int DiffMask;
        public int DestX1;
        public int DestY1;

        public void Read(ReadOnlySpan<byte> r)
        {
            Kind = BinaryPrimitives.ReadInt32LittleEndian(r);
            X = BinaryPrimitives.ReadUInt16LittleEndian(r[0x04..]);
            Y = BinaryPrimitives.ReadUInt16LittleEndian(r[0x06..]);
            Type = BinaryPrimitives.ReadUInt16LittleEndian(r[0x08..]);
            Amount = BinaryPrimitives.ReadUInt16LittleEndian(r[0x0a..]);
            DiffMask = BinaryPrimitives.ReadUInt16LittleEndian(r[0x0c..]);
            DestX1 = BinaryPrimitives.ReadUInt16LittleEndian(r[0x0e..]);
            DestY1 = BinaryPrimitives.ReadUInt16LittleEndian(r[0x10..]);
        }

        public void CopyFrom(LevelObject o)
        {
            var buffer = new byte[Size];
            o.Write(buffer);
            Read(buffer);
        }

        public void Write(Span<byte> w)
        {
            BinaryPrimitives.WriteInt32LittleEndian(w, Kind);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x04..], (ushort)X);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x06..], (ushort)Y);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x08..], (ushort)Type);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x0a..], (ushort)Amount);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x0c..], (ushort)DiffMask);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x0e..], (ushort)DestX1);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x10..], (ushort)DestY1);
        }
    }

    /// <summary>A bomb on its fuse.</summary>
    internal sealed class TimedObject
    {
        public const int Size = 0x16;
        public const int Count = 50;

        public int Active;
        public int NetOwner;
        public int Type;
        public int ObjSlot;
        public int Fuse;
        public int Variant;
        public int ExplLeft;
        public int Triggered;

        public void Clear()
        {
            Active = NetOwner = Type = ObjSlot = Fuse = Variant = ExplLeft = Triggered = 0;
        }

        public void Read(ReadOnlySpan<byte> r)
        {
            Active = BinaryPrimitives.ReadInt32LittleEndian(r);
            NetOwner = BinaryPrimitives.ReadInt32LittleEndian(r[0x04..]);
            Type = BinaryPrimitives.ReadUInt16LittleEndian(r[0x08..]);
            ObjSlot = BinaryPrimitives.ReadUInt16LittleEndian(r[0x0a..]);
            Fuse = BinaryPrimitives.ReadUInt16LittleEndian(r[0x0c..]);
            Variant = BinaryPrimitives.ReadUInt16LittleEndian(r[0x0e..]);
            ExplLeft = BinaryPrimitives.ReadUInt16LittleEndian(r[0x10..]);
            Triggered = BinaryPrimitives.ReadInt32LittleEndian(r[0x12..]);
        }

        public void Write(Span<byte> w)
        {
            BinaryPrimitives.WriteInt32LittleEndian(w, Active);
            BinaryPrimitives.WriteInt32LittleEndian(w[0x04..], NetOwner);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x08..], (ushort)Type);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x0a..], (ushort)ObjSlot);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x0c..], (ushort)Fuse);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x0e..], (ushort)Variant);
            BinaryPrimitives.WriteUInt16LittleEndian(w[0x10..], (ushort)ExplLeft);
            BinaryPrimitives.WriteInt32LittleEndian(w[0x12..], Triggered);
        }
    }
}
