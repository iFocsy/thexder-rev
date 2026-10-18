namespace Thexder.Core.Game
{
    /// <summary>
    /// Reproducing it keeps enemy behaviour statistically identical and makes the recorded demo scripts (THEX##.TSC,
    /// which store their seed) play back the same way.
    /// </summary>
    internal sealed class GameRandom
    {
        public const int RandMax = 0x7fff;

        private uint seed;

        public GameRandom(uint seed = 1)
        {
            this.seed = seed;
        }

        public uint Seed
        {
            get => seed;
            set => seed = value;
        }

        public void Reseed(uint value) => seed = value;

        /// <summary>rand(): 0..32767.</summary>
        public int Next()
        {
            seed = unchecked(seed * 214013u + 2531011u);
            return (int)((seed >> 16) & 0x7fff);
        }

        public int Next(int n) => Next() % n;
    }
}
