using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Thexder.Core.Game;

namespace Thexder.Feature.Procedural
{
    /// <summary>
    /// A generated campaign (an arc of worlds, five levels each, a boss at the end of each world) or one map.
    /// </summary>
    internal enum ProcKind
    {
        Campaign,
        Single,
    }

    /// <summary>
    /// A generated game's settings and progress, as kept in its save file (JSON, next to the player's saves).
    /// </summary>
    internal sealed class ProcSave
    {
        public int Version { get; set; } = 1;

        public ProcKind Kind { get; set; }

        public ulong Seed { get; set; }

        public MapSize Size { get; set; } = MapSize.Standard;

        public int Difficulty { get; set; } = 2;

        /// <summary>Campaign: worlds to play (0 = endless).</summary>
        public int Length { get; set; } = 10;

        /// <summary>Single map: its planet (1..10).</summary>
        public int Theme { get; set; } = 1;

        /// <summary>Single map: it ends in the planet's boss arena.</summary>
        public bool Boss { get; set; }

        public int Stage { get; set; } = 1;

        public int Level { get; set; } = 1;

        public bool Started { get; set; }

        public bool Completed { get; set; }

        public long TotalScore { get; set; }

        public int LevelsDone { get; set; }

        /// <summary>
        /// The player's stats when the current world began: weapon, energy, max energy, inventory (base64).
        /// </summary>
        public int[]? WorldStart { get; set; }

        public string? Inventory { get; set; }

        public DateTime Saved { get; set; }
    }

    /// <summary>
    /// A generated game being played: answers the game core's questions (which level, what comes next) and keeps the save
    /// file.
    /// </summary>
    internal sealed class ProcGame : IProceduralGame
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

        private readonly ClassicLibrary lib;
        private readonly string file;
        private readonly Func<int, string> planetName;

        private ProcGame(ProcSave save, ClassicLibrary lib, string file, Func<int, string> planetName)
        {
            Settings = save;
            this.lib = lib;
            this.file = file;
            this.planetName = planetName;
        }

        public ProcSave Settings { get; }

        public string SavePrefix => Settings.Kind == ProcKind.Campaign ? "arc" : "map";

        public int World => Settings.Kind == ProcKind.Campaign ? (Settings.Stage - 1) % 10 + 1 : Math.Clamp(Settings.Theme, 1, 10);

        public int Level => Settings.Level;

        public int Stage => Settings.Kind == ProcKind.Campaign ? Settings.Stage : World;

        public int Difficulty => Settings.Difficulty;

        public bool IsNew => !Settings.Started;

        public bool PlaysBriefings => Settings.Kind == ProcKind.Campaign;

        /// <summary>The save file of a player's generated game of this kind.</summary>
        public static string FileFor(string saveDirectory, string player, ProcKind kind) =>
            Path.Combine(saveDirectory, $"{player}~{(kind == ProcKind.Campaign ? "arc" : "map")}.thp");

        public static ProcGame Create(ProcSave settings, ClassicLibrary lib, string saveDirectory, string player, Func<int, string> planetName)
        {
            settings.Stage = 1;
            settings.Level = settings.Kind == ProcKind.Single ? (settings.Boss ? 5 : 3) : 1;
            settings.Started = false;
            settings.Completed = false;
            return new ProcGame(settings, lib, FileFor(saveDirectory, player, settings.Kind), planetName);
        }

        /// <summary>The player's saved game of this kind, if there is one that is not finished.</summary>
        public static ProcGame? Load(ClassicLibrary lib, string saveDirectory, string player, ProcKind kind, Func<int, string> planetName)
        {
            string path = FileFor(saveDirectory, player, kind);
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                var save = JsonSerializer.Deserialize<ProcSave>(File.ReadAllText(path), Json);
                return save == null || save.Completed || save.Kind != kind ? null : new ProcGame(save, lib, path, planetName);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not read {Path}", path);
                return null;
            }
        }

        /// <summary>Puts the saved world-start stats back into the game (for a continued game).</summary>
        public void RestoreInto(ThexderGame game)
        {
            var ws = game.ProcWorldStart;
            if (Settings.WorldStart is { Length: >= 3 } s)
            {
                ws.Weapon = s[0];
                ws.Energy = s[1];
                ws.MaxEnergy = s[2];
                if (Settings.Inventory != null)
                {
                    byte[] bytes = Convert.FromBase64String(Settings.Inventory);
                    if (bytes.Length >= Inventory.ByteSize)
                    {
                        ws.Inventory.Read(bytes);
                    }
                }
            }
            else
            {
                ws.Energy = 0; // None recorded: a fresh start
            }
        }

        public string Title
        {
            get
            {
                string planet = planetName(World);
                if (Settings.Kind == ProcKind.Single)
                {
                    return $"{planet} - Random map";
                }
                string arc = Settings.Length > 0 ? $"world {Settings.Stage} of {Settings.Length}" : $"world {Settings.Stage}";
                return $"{planet} - Level {Settings.Level} ({arc})";
            }
        }

        public LevelData BuildLevel()
        {
            ulong seed = LevelSeed(Settings.Seed, Settings.Stage, Settings.Level);
            var req = new GenRequest(seed, World, Settings.Level, Settings.Size, Settings.Difficulty, Stage, Title);
            return LevelGenerator.Generate(req, lib);
        }

        /// <summary>The seed of one level of a game.</summary>
        public static ulong LevelSeed(ulong seed, int stage, int level)
        {
            ulong x = seed ^ ((ulong)stage << 32) ^ (ulong)level * 0x9E3779B97F4A7C15UL;
            x ^= x >> 33;
            x *= 0xff51afd7ed558ccdUL;
            x ^= x >> 33;
            return x;
        }

        public ProcStep Finish(uint score)
        {
            Settings.TotalScore += score;
            Settings.LevelsDone++;
            ProcStep step;
            if (Settings.Kind == ProcKind.Single)
            {
                Settings.Completed = true;
                step = ProcStep.Finished;
            }
            else if (Settings.Level < 5)
            {
                Settings.Level++;
                step = ProcStep.NextLevel;
            }
            else if (Settings.Length > 0 && Settings.Stage >= Settings.Length)
            {
                Settings.Completed = true;
                step = ProcStep.Finished;
            }
            else
            {
                Settings.Stage++;
                Settings.Level = 1;
                step = ProcStep.NextWorld;
            }
            Write();
            return step;
        }

        public void RestartWorld() => Settings.Level = 1;

        public void Save(ThexderGame game)
        {
            Settings.Started = true;
            var ws = game.ProcWorldStart;
            Settings.WorldStart = new[] { ws.Weapon, ws.Energy, ws.MaxEnergy };
            byte[] inventory = new byte[Inventory.ByteSize];
            ws.Inventory.Write(inventory);
            Settings.Inventory = Convert.ToBase64String(inventory);
            Write();
        }

        private void Write()
        {
            Settings.Saved = DateTime.Now;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, JsonSerializer.Serialize(Settings, Json));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not save {Path}", file);
            }
        }

        /// <summary>A new random seed (shown to the player, who can type it again to replay a map).</summary>
        public static ulong NewSeed() => (ulong)Random.Shared.NextInt64(1, 999_999_999);
    }
}
