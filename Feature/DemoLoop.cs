using System.Buffers.Binary;
using Thexder.Core.Assets;
using Thexder.Core.Game;

namespace Thexder.Feature
{
    /// <summary>
    /// The "/X" kiosk demo loop: for n = 1..30, the title card DEMO&lt;n&gt;.BMP for 5 seconds, then the recorded game
    /// THEX&lt;n&gt;.TSC driven by the normal game ticks, a 250 ms pause, and round again until the program is closed.
    /// </summary>
    internal sealed class DemoLoop
    {
        private const int Count = 30;
        private const int CardMs = 5000;
        private const int GapMs = 250;

        private readonly ThexderGame game;
        private readonly string dataDirectory;
        private readonly IGameView view;
        private Step step = Step.NotStarted;
        private int number;
        private long until;

        public DemoLoop(ThexderGame game, string dataDirectory, IGameView view)
        {
            this.game = game;
            this.dataDirectory = dataDirectory;
            this.view = view;
        }

        private enum Step
        {
            NotStarted,
            Card,
            Playing,
            Gap,
            Idle,
        }

        /// <summary>Advances the loop; called before the game ticks of every rendered frame.</summary>
        public void Update(long nowMs)
        {
            switch (step)
            {
                case Step.NotStarted:
                    game.KioskInit();
                    game.PauseCount++; // The demo thread holds the game paused except while a script plays
                    Next(nowMs);
                    break;
                case Step.Card when nowMs >= until:
                    view.HideDemoCard();
                    if (!Play())
                    {
                        Next(nowMs);
                    }
                    break;
                case Step.Playing when !game.DemoMode:
                    game.PauseCount++;
                    step = Step.Gap;
                    until = nowMs + GapMs;
                    break;
                case Step.Gap when nowMs >= until:
                    Next(nowMs);
                    break;
            }
        }

        /// <summary>The next card, or straight to the next script when its card is missing.</summary>
        private void Next(long nowMs)
        {
            for (int tries = 0; tries < Count; tries++)
            {
                number = number % Count + 1;
                if (ShowCard(number))
                {
                    step = Step.Card;
                    until = nowMs + CardMs;
                    return;
                }
                if (Play())
                {
                    return;
                }
            }
            step = Step.Idle; // No demo files at all
        }

        private bool Play()
        {
            if (game.StartDemo(number) && game.PauseCount == 1)
            {
                game.PauseCount--;
                step = Step.Playing;
                return true;
            }
            return false;
        }

        /// <summary>
        /// DEMO&lt;n&gt;.BMP: 320 x 336, 8 bits with its own palette, rows stored top-down like the game's DIBs.
        /// </summary>
        private bool ShowCard(int n)
        {
            string path = Path.Combine(dataDirectory, $"DEMO{n:00}.BMP");
            if (!File.Exists(path))
            {
                return false;
            }
            byte[] file = File.ReadAllBytes(path);
            if (file.Length < 14 + 40 || file[0] != 'B' || file[1] != 'M')
            {
                return false;
            }
            var image = IndexedImage.FromDib(file.AsSpan(14));
            uint[] colors = (image.Palette ?? new Palette()).Colors;
            var bgra = new byte[image.Width * image.Height * 4];
            for (int i = 0; i < image.Pixels.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bgra.AsSpan(i * 4), colors[image.Pixels[i]]);
            }
            view.ShowDemoCard(bgra, image.Width, image.Height);
            return true;
        }
    }
}
