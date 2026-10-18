namespace Thexder.Core.Game
{
    /// <summary>An explosion of a bomb (sizes and offsets in cells).</summary>
    internal sealed record BombExplosion(string Sprite, int W, int H, int XOff, int YOff, int Frames, int Sound);

    /// <summary>
    /// The item table: which forms can use a weapon or piece of equipment, weapon damage, bomb data and equipment
    /// durations.
    /// </summary>
    internal sealed class ItemDef
    {
        /// <summary>Usable in walker, plane and tank form.</summary>
        public bool[] Usable { get; init; } = { true, true, true };

        public int Damage { get; init; }

        /// <summary>Jet Pack speed bonus (item 17).</summary>
        public int JetpackBonus { get; init; }

        /// <summary>Jump Boots jump height bonus (item 20).</summary>
        public int JumpBonus { get; init; }

        public int BlastRadius { get; init; }

        /// <summary>Bombs: fuse in 6-frame units.</summary>
        public int Timer { get; init; }

        public string? BombSprite { get; init; }

        public int BombW { get; init; }

        public int BombH { get; init; }

        public BombExplosion[] Explosions { get; init; } = Array.Empty<BombExplosion>();
    }

    internal static class ItemTable
    {
        public const int Count = 43;

        public static ItemDef[] Build()
        {
            var t = new ItemDef[Count];
            for (int i = 0; i < Count; i++)
            {
                t[i] = new ItemDef();
            }

            t[1] = new ItemDef { Usable = new[] { true, true, false }, Damage = 1 };
            t[2] = new ItemDef { Usable = new[] { true, false, false }, Damage = 10 };
            t[3] = new ItemDef { Usable = new[] { true, false, false }, Damage = 20 };
            t[4] = new ItemDef { Usable = new[] { true, false, false }, Damage = 25 };
            t[5] = new ItemDef { Usable = new[] { true, false, false }, Damage = 50 };
            t[6] = new ItemDef
            {
                Damage = 10, BlastRadius = 6, Timer = 5, BombSprite = "SmallBomb", BombW = 2, BombH = 2,
                Explosions = new[]
                {
                    new BombExplosion("Expl2A", 6, 7, 3, 7, 30, 0xc6),
                    new BombExplosion("Expl2B", 6, 7, 3, 7, 15, 0xbe),
                },
            };
            t[7] = new ItemDef
            {
                Usable = new[] { false, false, true }, Damage = 60, BlastRadius = 10, Timer = 5, BombSprite = "BigBomb", BombW = 2, BombH = 2,
                Explosions = new[]
                {
                    new BombExplosion("Expl1B", 12, 14, 6, 14, 15, 0xc2),
                    new BombExplosion("Expl1C", 12, 14, 5, 14, 15, 0xc0),
                },
            };
            t[8] = new ItemDef { Usable = new[] { false, true, false }, Damage = 1 };
            t[17] = new ItemDef { JetpackBonus = 4, Timer = 30 };
            t[20] = new ItemDef { JumpBonus = 4, Timer = 30 };
            t[23] = new ItemDef { Timer = 30 };
            t[24] = new ItemDef { Timer = 30 };
            t[25] = new ItemDef { Timer = 30 };
            t[27] = new ItemDef { Timer = 20 };
            t[28] = new ItemDef { Timer = 20 };
            t[29] = new ItemDef { Timer = 30 };
            t[30] = new ItemDef { Timer = 30 };
            t[31] = new ItemDef { Timer = 20 };
            t[32] = new ItemDef { Timer = 20 };
            t[0x21] = new ItemDef
            {
                Damage = 50, BlastRadius = 1, Timer = 1, BombSprite = "DemonGrenade", BombW = 2, BombH = 2,
                Explosions = new[]
                {
                    new BombExplosion("Flak1B", 10, 9, 5, 5, 20, 0xc4),
                    new BombExplosion("Flak1D", 10, 9, 5, 5, 20, 0xc1),
                },
            };
            t[0x22] = new ItemDef
            {
                Damage = 10, BlastRadius = 10, Timer = 3, BombSprite = "PrimusBomb", BombW = 2, BombH = 2,
                Explosions = new[] { new BombExplosion("Shok1B", 16, 14, 11, 11, 15, 4) },
            };
            return t;
        }
    }
}
