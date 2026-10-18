using Thexder.Core.Assets;

namespace Thexder.Core.Engine
{
    /// <summary>Lights, lava and water in the art use these entries.</summary>
    internal sealed class PaletteCycler
    {
        // {period ms, first range, end range}
        private static readonly (int Period, int First, int End)[] Timers =
        {
            (1000, 0, 3),
            (150, 3, 6),
            (125, 6, 8),
            (100, 8, 9),
        };

        // {first index, count}
        private static readonly (int First, int Count)[] Ranges =
        {
            (10, 2), (12, 2), (14, 2),
            (224, 4), (228, 4), (243, 3),
            (232, 4), (236, 4),
            (240, 3),
        };

        private readonly long[] accumulated = new long[Timers.Length];
        private long lastTick = -1;

        public PaletteCycler(Palette palette)
        {
            Palette = palette;
        }

        /// <summary>The live palette; its cycling entries change in place.</summary>
        public Palette Palette { get; }

        /// <summary>Restarts the timers; the next <see cref="Update"/> sets the time base.</summary>
        public void Reset()
        {
            lastTick = -1;
            Array.Clear(accumulated);
        }

        /// <summary>
        /// Advances the timers to <paramref name="nowMs"/> (any millisecond clock, as long as it is always the same one);
        /// returns true when any entry changed.
        /// </summary>
        public bool Update(long nowMs)
        {
            if (lastTick < 0 || nowMs < lastTick)
            {
                lastTick = nowMs;
                return false;
            }
            long dt = nowMs - lastTick;
            lastTick = nowMs;
            bool changed = false;
            for (int t = 0; t < Timers.Length; t++)
            {
                accumulated[t] += dt;
                if (accumulated[t] >= Timers[t].Period)
                {
                    for (int r = Timers[t].First; r < Timers[t].End; r++)
                    {
                        RotateUp(Ranges[r].First, Ranges[r].Count);
                    }
                    accumulated[t] = 0;
                    changed = true;
                }
            }
            return changed;
        }

        private void RotateUp(int first, int count)
        {
            uint[] c = Palette.Colors;
            uint last = c[first + count - 1];
            Array.Copy(c, first, c, first + 1, count - 1);
            c[first] = last;
        }
    }
}
