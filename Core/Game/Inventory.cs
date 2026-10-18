using System.Buffers.Binary;

namespace Thexder.Core.Game
{
    /// <summary>
    /// The player's weapons and equipment: 33 entries of {s32 owned, u16 count, u16 active time}, indexed by object type
    /// (string id 309 + type).
    /// </summary>
    internal sealed class Inventory
    {
        public const int Size = 33;
        public const int ByteSize = Size * 8;

        public readonly int[] Owned = new int[Size];
        public readonly int[] Count = new int[Size];
        public readonly int[] Active = new int[Size];

        /// <summary>Owned and currently switched on.</summary>
        public bool IsActive(int type) => Owned[type] != 0 && Active[type] != 0;

        public int Ammo(int type) => Count[type];

        public void Clear()
        {
            Array.Clear(Owned);
            Array.Clear(Count);
            Array.Clear(Active);
        }

        public void CopyFrom(Inventory other)
        {
            Array.Copy(other.Owned, Owned, Size);
            Array.Copy(other.Count, Count, Size);
            Array.Copy(other.Active, Active, Size);
        }

        public void Read(ReadOnlySpan<byte> data)
        {
            for (int i = 0; i < Size; i++)
            {
                Owned[i] = BinaryPrimitives.ReadInt32LittleEndian(data[(i * 8)..]);
                Count[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 8 + 4)..]);
                Active[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 8 + 6)..]);
            }
        }

        public void Write(Span<byte> data)
        {
            for (int i = 0; i < Size; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(data[(i * 8)..], Owned[i]);
                BinaryPrimitives.WriteUInt16LittleEndian(data[(i * 8 + 4)..], (ushort)Count[i]);
                BinaryPrimitives.WriteUInt16LittleEndian(data[(i * 8 + 6)..], (ushort)Active[i]);
            }
        }
    }

    /// <summary>Object / item type numbers (string id 309 + type).</summary>
    internal static class ItemType
    {
        public const int Laser = 1;
        public const int Fireball = 2;
        public const int Grenade = 3;
        public const int AspMissile = 4;
        public const int DevastatorMissile = 5;
        public const int ThermalBomb = 6;
        public const int PlasmaBomb = 7;
        public const int FanFireLaser = 8;
        public const int JetPack = 17;
        public const int EnergyCell = 18;
        public const int Transporter = 19;
        public const int JumpBoots = 20;
        public const int Charge = 21;
        public const int ChargeExtender = 22;
        public const int ShotArmor = 23;
        public const int LavaArmor = 24;
        public const int AcidArmor = 25;
        public const int PlatformSwitch = 26;
        public const int ViewObjects = 27;
        public const int ViewCells = 28;
        public const int MegaArmor = 29;
        public const int HoverPack = 30;
        public const int ViewShootAway = 31;
        public const int ViewForeground = 32;

        /// <summary>The equipment list shown in the Equipment window.</summary>
        public static readonly int[] EquipmentList = { 17, 20, 23, 24, 25, 27, 28, 29, 30, 31, 32 };
    }
}
