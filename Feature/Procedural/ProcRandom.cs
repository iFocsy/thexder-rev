namespace Thexder.Feature.Procedural
{
    /// <summary>
    /// The generator's random numbers: xoshiro256** seeded through SplitMix64, so a seed builds the same level on every
    /// machine and every .NET version (System.Random makes no such promise).
    /// </summary>
    internal sealed class ProcRandom
    {
        private ulong s0, s1, s2, s3;

        public ProcRandom(ulong seed)
        {
            ulong x = seed;
            s0 = SplitMix(ref x);
            s1 = SplitMix(ref x);
            s2 = SplitMix(ref x);
            s3 = SplitMix(ref x);
        }

        /// <summary>A generator for a named part of the level (layout, enemies...), independent of the others.</summary>
        public static ProcRandom For(ulong seed, string part)
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in part)
            {
                h = (h ^ c) * 1099511628211UL;
            }
            return new ProcRandom(seed ^ h);
        }

        private static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        public ulong NextULong()
        {
            ulong result = Rotl(s1 * 5, 7) * 9;
            ulong t = s1 << 17;
            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;
            s2 ^= t;
            s3 = Rotl(s3, 45);
            return result;
        }

        /// <summary>0.. n-1 (0 when n &lt;= 1).</summary>
        public int Next(int n) => n <= 1 ? 0 : (int)(NextULong() % (ulong)n);

        /// <summary>min.. max inclusive.</summary>
        public int Range(int min, int max) => max <= min ? min : min + Next(max - min + 1);

        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        public bool Chance(double p) => NextDouble() < p;

        public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

        /// <summary>An index chosen in proportion to the weights (the last one when they are all zero).</summary>
        public int Weighted(IReadOnlyList<double> weights)
        {
            double total = 0;
            foreach (double w in weights)
            {
                total += Math.Max(0, w);
            }
            double r = NextDouble() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                r -= Math.Max(0, weights[i]);
                if (r < 0)
                {
                    return i;
                }
            }
            return weights.Count - 1;
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
