namespace Thexder.Core.Game
{
    /// <summary>One explosion variant of a shot (sizes and offsets in pixels at the current scale).</summary>
    internal sealed record ShotExplosion(string Sprite, int W, int H, int XOff, int YOff, int Frames, int Sound);

    /// <summary>A shot type.</summary>
    internal sealed class ShotType
    {
        public bool Defined { get; init; }

        /// <summary>Movement steps (pixels) per frame.</summary>
        public int Speed { get; init; }

        /// <summary>Aimed with the eight direction keys (no auto-target for the player).</summary>
        public bool Aimed { get; init; }

        public string? MuzzleSprite { get; init; }

        public int FireSound { get; init; } = 0xffff;

        public int MuzzleFrames { get; init; }

        public int MuzzleW { get; init; }

        public int MuzzleH { get; init; }

        public int ImpactSound { get; init; } = 0xffff;

        public string? FlySpriteL { get; init; }

        public string? FlySpriteR { get; init; }

        public int FlyFrames { get; init; }

        public int FlyW { get; init; }

        public int FlyH { get; init; }

        /// <summary>Cells of shoot-away destroyed around the impact (and player damage for enemy shots).</summary>
        public int BlastRadius { get; init; }

        public ShotExplosion[] Explosions { get; init; } = Array.Empty<ShotExplosion>();
    }

    internal static class ShotTypes
    {
        public const int Count = 41;

        // Movement classes.
        public static bool IsHitscan(int type) => type is 1 or 8 or 0x11 or 0x22 or 0x23;

        public static bool IsArc(int type) => type == 3;

        public static bool IsTrail(int type) => type == 0x1b;

        public static bool IsDirectionalMissile(int type) => type is 4 or 5 or 0x24;

        public static bool IsRubble(int type) => type is >= 0x15 and <= 0x1a;

        public static ShotType[] Build(int s)
        {
            var t = new ShotType[Count];
            for (int i = 0; i < Count; i++)
            {
                t[i] = new ShotType();
            }

            t[1] = new ShotType { Defined = true, FireSound = 0x10, MuzzleSprite = "MuzzleFlashA", MuzzleFrames = 1, MuzzleW = s, MuzzleH = s };
            t[2] = new ShotType
            {
                Defined = true, Speed = 8, FireSound = 0x48, FlySpriteL = "Fireball", FlyFrames = 1, FlyW = s, FlyH = s, BlastRadius = 3,
                Explosions = new[]
                {
                    new ShotExplosion("ZeroC", 4 * s, 4 * s, 3 * s, 3 * s, 9, 0xc5),
                    new ShotExplosion("ZeroD", 4 * s, 4 * s, s, 3 * s, 9, 0xbf),
                },
            };
            t[3] = new ShotType
            {
                Defined = true, Speed = 12, Aimed = true, FireSound = 0x46, FlySpriteL = "Grenade", FlyFrames = 4, FlyW = 2 * s, FlyH = 2 * s, BlastRadius = 6,
                Explosions = new[]
                {
                    new ShotExplosion("Flak1F", 10 * s, 9 * s, 5 * s, 5 * s, 10, 0xbf),
                    new ShotExplosion("Flak1G", 10 * s, 9 * s, 5 * s, 5 * s, 10, 0xc9),
                },
            };
            t[4] = new ShotType
            {
                Defined = true, Speed = 8, Aimed = true, FireSound = 0x49, MuzzleSprite = "MuzzleFlashC", MuzzleFrames = 1, MuzzleW = s, MuzzleH = s,
                FlySpriteL = "SmallMissle", FlyFrames = 1, FlyW = 2 * s, FlyH = 2 * s, BlastRadius = 8,
                Explosions = new[]
                {
                    new ShotExplosion("ZeroH", 7 * s, 6 * s, 3 * s, 4 * s, 12, 0xbe),
                    new ShotExplosion("ZeroI", 7 * s, 6 * s, 4 * s, 4 * s, 12, 0xc9),
                },
            };
            t[5] = new ShotType
            {
                Defined = true, Speed = 8, Aimed = true, FireSound = 0x49, MuzzleSprite = "MuzzleFlashD", MuzzleFrames = 1, MuzzleW = s, MuzzleH = s,
                FlySpriteL = "BigMissle", FlyFrames = 1, FlyW = 2 * s, FlyH = 2 * s, BlastRadius = 10,
                Explosions = new[]
                {
                    new ShotExplosion("Flak1E", 10 * s, 9 * s, 5 * s, 5 * s, 10, 0xbe),
                    new ShotExplosion("Flak1H", 10 * s, 9 * s, 5 * s, 5 * s, 10, 0xc9),
                },
            };
            t[8] = new ShotType { Defined = true, FireSound = 0x47, MuzzleSprite = "MuzzleFlashB", MuzzleFrames = 1, MuzzleW = s, MuzzleH = s };
            t[0x11] = new ShotType { Defined = true, MuzzleSprite = "MuzzleFlashA", MuzzleFrames = 1, MuzzleW = s, MuzzleH = s };
            t[0x12] = new ShotType
            {
                Defined = true, Speed = 8, FireSound = 0x52, FlySpriteL = "BugSnakeFireball", FlyFrames = 1, FlyW = 3 * s, FlyH = 3 * s,
                Explosions = new[]
                {
                    new ShotExplosion("Flak1D", 10 * s, 9 * s, 5 * s, 5 * s, 20, 0xc5),
                    new ShotExplosion("Flak2B", 8 * s, 7 * s, 4 * s, 3 * s, 30, 0xbf),
                },
            };
            t[0x13] = new ShotType { Defined = true, Speed = 8, FireSound = 0x3d, FlySpriteL = "DaggerL", FlySpriteR = "DaggerR", FlyFrames = 6, FlyW = 3 * s, FlyH = 3 * s };
            t[0x14] = new ShotType { Defined = true, Speed = 8, FireSound = 0x3d, FlySpriteL = "BoomerangL", FlySpriteR = "BoomerangR", FlyFrames = 4, FlyW = 2 * s, FlyH = 2 * s };
            t[0x15] = Rubble("RubbleA", s, 0);
            t[0x16] = Rubble("RubbleB", s, 0);
            t[0x17] = Rubble("RubbleC", 2 * s, 1);
            t[0x18] = Rubble("RubbleD", 2 * s, 1);
            t[0x19] = Rubble("RubbleE", 3 * s, 2);
            t[0x1a] = Rubble("RubbleF", 3 * s, 2);
            t[0x1b] = new ShotType { Defined = true, Speed = 2 * s, Aimed = true, FireSound = 0x48, FlySpriteL = "AcidStream", FlyFrames = 4, FlyW = 2 * s, FlyH = 2 * s, BlastRadius = 2 };
            t[0x1c] = new ShotType
            {
                Defined = true, Speed = 8, Aimed = true, FireSound = 0x8c, FlySpriteL = "FireballDragon", FlyFrames = 4, FlyW = 3 * s, FlyH = 3 * s,
                Explosions = new[]
                {
                    new ShotExplosion("Flak1B", 10 * s, 9 * s, 5 * s, 5 * s, 20, 0xc5),
                    new ShotExplosion("Flak2A", 8 * s, 7 * s, 4 * s, 3 * s, 30, 0xc4),
                },
            };
            t[0x1d] = new ShotType
            {
                Defined = true, Speed = 16, Aimed = true, FireSound = 0x8a, ImpactSound = 0x2c, FlySpriteL = "Plasma", FlyFrames = 4, FlyW = 2 * s, FlyH = 2 * s,
                Explosions = new[]
                {
                    new ShotExplosion("ZeroE", 4 * s, 4 * s, s, 3 * s, 18, 0xc5),
                    new ShotExplosion("Flak2C", 8 * s, 7 * s, 4 * s, 3 * s, 30, 0xbf),
                },
            };
            t[0x1e] = new ShotType
            {
                Defined = true, Speed = 2 * s, Aimed = true, FireSound = 0x89, FlySpriteL = "TriTurretMissle", FlyFrames = 1, FlyW = s, FlyH = s,
                Explosions = new[]
                {
                    new ShotExplosion("Flak1A", 10 * s, 9 * s, 5 * s, 5 * s, 20, 0xc5),
                    new ShotExplosion("Flak1C", 10 * s, 9 * s, 5 * s, 5 * s, 20, 0xbf),
                },
            };
            t[0x21] = new ShotType { Defined = true, Speed = 2 * s, Aimed = true, FireSound = 0x89, FlySpriteL = "AcidStar", FlyFrames = 4, FlyW = 2 * s, FlyH = 2 * s };
            t[0x22] = new ShotType { Defined = true };
            t[0x23] = new ShotType { Defined = true, FireSound = 0x2f, MuzzleSprite = "MuzzleFlashE", MuzzleFrames = 1, MuzzleW = s, MuzzleH = s };
            t[0x24] = new ShotType
            {
                Defined = true, Speed = 8, Aimed = true, FireSound = 0x49, MuzzleSprite = "MuzzleFlashF", MuzzleFrames = 1, MuzzleW = s, MuzzleH = s,
                FlySpriteL = "PrimusMissle", FlyFrames = 1, FlyW = 2 * s, FlyH = 2 * s,
                Explosions = new[]
                {
                    new ShotExplosion("Flak1I", 10 * s, 9 * s, 5 * s, 5 * s, 10, 0xc4),
                    new ShotExplosion("Flak1J", 10 * s, 9 * s, 5 * s, 5 * s, 10, 0xbe),
                },
            };
            return t;
        }

        private static ShotType Rubble(string sprite, int size, int radius) => new()
        {
            Defined = true, Speed = 12, Aimed = true, FireSound = 0x46, ImpactSound = 0x2c,
            FlySpriteL = sprite, FlyFrames = 8, FlyW = size, FlyH = size, BlastRadius = radius,
        };
    }
}
