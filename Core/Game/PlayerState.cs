using System.Buffers.Binary;

namespace Thexder.Core.Game
{
    /// <summary>The Thexder robot.</summary>
    internal sealed class PlayerState
    {
        public const int RecordSize = 0x42;

        public const int Walker = 0;
        public const int Plane = 1;
        public const int Tank = 2;

        public const uint FlagTransforming = 0x01;
        public const uint FlagFlinch = 0x02;
        public const uint FlagTurning = 0x04;
        public const uint FlagDying = 0x08;
        public const uint FlagTransportOut = 0x10;
        public const uint FlagTransportIn = 0x20;
        public const uint FlagGoingHome = 0x40;

        public const int DirLeft = 1;
        public const int DirRight = 2;
        public const int DirUp = 4;
        public const int DirDown = 8;

        public int X;
        public int Y;
        public int Form;
        public int Frame;
        public uint StateFlags;
        public int TransformTarget;
        public int Speed;
        public int SavedSpeed;
        public int MoveEnable;
        public int SpriteStride;
        public int CmdDir;
        public int PlaneDir;
        public int LastDir;
        public int Falling;
        public int PhysicsOn;
        public int Facing;
        public int PrevFacing;
        public int Jumping;
        public int JumpCount;
        public int Landed;
        public int SpriteSet;
        public uint FramePointer;
        public int Weapon;
        public int Energy;
        public int AltWeapon;
        public uint Score;
        public int HitFlash;
        public int MaxEnergy;
        public int Reserved;
        public int PlayState;
        public uint LevelFrames;

        /// <summary>
        /// True while a transform, turn, death or transporter animation owns the robot (flags &amp; 0x3d).
        /// </summary>
        public bool Busy => (StateFlags & 0x3d) != 0;

        public void Read(ReadOnlySpan<byte> r)
        {
            X = U16(r, 0x00);
            Y = U16(r, 0x02);
            Form = U16(r, 0x04);
            Frame = (short)U16(r, 0x06);
            StateFlags = BinaryPrimitives.ReadUInt32LittleEndian(r[0x08..]);
            TransformTarget = U16(r, 0x0c);
            Speed = U16(r, 0x0e);
            SavedSpeed = U16(r, 0x10);
            MoveEnable = U16(r, 0x12);
            SpriteStride = U16(r, 0x14);
            CmdDir = U16(r, 0x16);
            PlaneDir = U16(r, 0x18);
            LastDir = U16(r, 0x1a);
            Falling = U16(r, 0x1c);
            PhysicsOn = U16(r, 0x1e);
            Facing = U16(r, 0x20);
            PrevFacing = U16(r, 0x22);
            Jumping = U16(r, 0x24);
            JumpCount = U16(r, 0x26);
            Landed = U16(r, 0x28);
            SpriteSet = U16(r, 0x2a);
            FramePointer = BinaryPrimitives.ReadUInt32LittleEndian(r[0x2c..]);
            Weapon = U16(r, 0x30);
            Energy = U16(r, 0x32);
            AltWeapon = U16(r, 0x34);
            Score = BinaryPrimitives.ReadUInt32LittleEndian(r[0x36..]);
            HitFlash = r[0x3a];
            MaxEnergy = r[0x3b];
            Reserved = r[0x3c];
            PlayState = r[0x3d];
            LevelFrames = BinaryPrimitives.ReadUInt32LittleEndian(r[0x3e..]);
        }

        public void Write(Span<byte> w)
        {
            W16(w, 0x00, X);
            W16(w, 0x02, Y);
            W16(w, 0x04, Form);
            W16(w, 0x06, Frame);
            BinaryPrimitives.WriteUInt32LittleEndian(w[0x08..], StateFlags);
            W16(w, 0x0c, TransformTarget);
            W16(w, 0x0e, Speed);
            W16(w, 0x10, SavedSpeed);
            W16(w, 0x12, MoveEnable);
            W16(w, 0x14, SpriteStride);
            W16(w, 0x16, CmdDir);
            W16(w, 0x18, PlaneDir);
            W16(w, 0x1a, LastDir);
            W16(w, 0x1c, Falling);
            W16(w, 0x1e, PhysicsOn);
            W16(w, 0x20, Facing);
            W16(w, 0x22, PrevFacing);
            W16(w, 0x24, Jumping);
            W16(w, 0x26, JumpCount);
            W16(w, 0x28, Landed);
            W16(w, 0x2a, SpriteSet);
            BinaryPrimitives.WriteUInt32LittleEndian(w[0x2c..], 0);
            W16(w, 0x30, Weapon);
            W16(w, 0x32, Energy);
            W16(w, 0x34, AltWeapon);
            BinaryPrimitives.WriteUInt32LittleEndian(w[0x36..], Score);
            w[0x3a] = (byte)HitFlash;
            w[0x3b] = (byte)MaxEnergy;
            w[0x3c] = (byte)Reserved;
            w[0x3d] = (byte)PlayState;
            BinaryPrimitives.WriteUInt32LittleEndian(w[0x3e..], LevelFrames);
        }

        private static int U16(ReadOnlySpan<byte> r, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(r[offset..]);

        private static void W16(Span<byte> w, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(w[offset..], (ushort)value);
    }
}
