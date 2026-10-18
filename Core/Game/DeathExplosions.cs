namespace Thexder.Core.Game
{
    /// <summary>
    /// An enemy death explosion: a strip of <see cref="Frames"/> frames of W x H cells, centred on (CX, CY) cells, with a
    /// sound on its first frame.
    /// </summary>
    internal sealed record DeathExplosion(string Sprite, int W, int H, int CX, int CY, int Frames, int Sound);

    /// <summary>The 21 death explosions referenced by the enemy types.</summary>
    internal static class DeathExplosions
    {
        public const int Count = 21;

        public static readonly DeathExplosion[] Table =
        {
            new("Shok1a", 16, 14, 7, 7, 30, 0xc2),
            new("Expl1a", 12, 14, 5, 14, 30, 0x04),
            new("Flak1a", 10, 9, 5, 5, 20, 0xc3),
            new("Flak1b", 10, 9, 5, 5, 20, 0xc0),
            new("Flak1c", 10, 9, 5, 5, 20, 0xc3),
            new("Flak1d", 10, 9, 5, 5, 20, 0xc3),
            new("Flak2a", 8, 7, 4, 3, 30, 0xc6),
            new("Flak2b", 8, 7, 4, 3, 30, 0xc6),
            new("Flak2c", 8, 7, 4, 3, 30, 0xc6),
            new("GutsA", 6, 6, 3, 3, 9, 0x91),
            new("GutsB", 6, 6, 3, 3, 15, 0xcd),
            new("GutsC", 9, 9, 4, 4, 15, 0xcd),
            new("GutsD", 9, 9, 4, 4, 15, 0xcd),
            new("GutsE", 9, 9, 4, 4, 15, 0x91),
            new("GutsF", 9, 9, 4, 4, 15, 0xcd),
            new("GutsG", 12, 12, 6, 6, 21, 0xcd),
            new("GutsH", 12, 12, 6, 6, 21, 0xcd),
            new("GutsI", 6, 6, 3, 3, 12, 0x91),
            new("ZeroE", 4, 4, 1, 3, 18, 0xc0),
            new("ZeroF", 4, 4, 2, 3, 18, 0xc0),
            new("ZeroG", 7, 6, 4, 4, 24, 0xc6),
        };
    }
}
