namespace Thexder.Services
{
    /// <summary>Command line options.</summary>
    internal sealed class GameOptions
    {
        public const string Usage = "Thexder.exe [--world <1-10> [--level <1-5>]] [--mute] [--fullscreen] [--kiosk]";

        public int? World { get; private set; }

        public int? Level { get; private set; }

        public bool Mute { get; private set; }

        public bool Fullscreen { get; private set; }

        /// <summary>Self-running demo mode: the recorded demos loop until Esc.</summary>
        public bool Kiosk { get; private set; }

        /// <summary>Testing: drive the window from a script and write its output to this folder.</summary>
        public string? UiScriptDir { get; private set; }

        public string? UiScript { get; private set; }

        public static GameOptions Parse(string[] args)
        {
            var options = new GameOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
                int NextInt(int min, int max)
                {
                    string value = Next();
                    return int.TryParse(value, out int n) && n >= min && n <= max
                        ? n
                        : throw new ArgumentException($"{arg} must be a number from {min} to {max}.");
                }

                switch (arg.ToLowerInvariant())
                {
                    case "--world": options.World = NextInt(1, 10); break;
                    case "--level": options.Level = NextInt(1, 5); break;
                    case "--mute": options.Mute = true; break;
                    case "--fullscreen": options.Fullscreen = true; break;
                    case "--kiosk": options.Kiosk = true; break;
                    case "--ui-script": options.UiScriptDir = Next(); break;
                    default:
                        if (options.UiScriptDir != null && options.UiScript == null && !arg.StartsWith("--", StringComparison.Ordinal))
                        {
                            options.UiScript = arg;
                            break;
                        }
                        throw new ArgumentException($"Unknown option {arg}.");
                }
            }
            return options;
        }
    }
}
