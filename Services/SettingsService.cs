using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Thexder.Utils;

namespace Thexder.Services
{
    /// <summary>The player's options, stored as JSON in data\settings.json.</summary>
    internal sealed class GameSettings
    {
        public string PlayerName { get; set; } = "";

        /// <summary>Logic updates per second, 5..30.</summary>
        public int UpdateSpeed { get; set; } = 15;

        /// <summary>0 = Low (THEX640), 1 = Medium (THEX800), 2 = High (THEX1024).</summary>
        public int Resolution { get; set; } = 2;

        /// <summary>Playfield width in cells: 40 (Standard) or 60.</summary>
        public int PlayfieldWidth { get; set; } = 60;

        public bool Sound { get; set; } = true;

        public bool Music { get; set; } = true;

        public bool Speech { get; set; } = true;

        /// <summary>Options > Performance: scrolling (parallax) background.</summary>
        public bool ScrollingBackground { get; set; } = true;

        public bool SmoothMotion { get; set; } = true;

        /// <summary>Options > Smart Laser Aim: the auto-aim skips targets behind walls.</summary>
        public bool SmartAim { get; set; } = true;

        public int Difficulty { get; set; } = 2;

        public bool ShowMap { get; set; } = true;

        public bool ShowWeapons { get; set; } = true;

        public bool ShowEquipment { get; set; } = true;

        public bool ShowEnemyStatus { get; set; } = true;

        public bool Fullscreen { get; set; }

        public double SoundVolume { get; set; } = 0.45;

        public double MusicVolume { get; set; } = 0.45;

        public WindowPlacement? Window { get; set; }

        /// <summary>Side windows that were undocked, by name, with their own place.</summary>
        public Dictionary<string, PanelPlacement>? Panels { get; set; }

        /// <summary>Scale of the docked Weapons / Equipment / Enemy Status column; 0 = fit to the monitor.</summary>
        public double PanelScale { get; set; }

        /// <summary>Height of the docked map's picture; 0 = 68 rows at the column's scale.</summary>
        public double MapHeight { get; set; }

        /// <summary>
        /// The last settings of the generated games' dialogs: map size (MapSize), campaign length (worlds, 0 = endless),
        /// difficulty, the random map's planet (0 = random) and whether it ends with a boss.
        /// </summary>
        public int ProcSize { get; set; } = 1;

        public int ProcLength { get; set; } = 10;

        public int ProcDifficulty { get; set; } = 2;

        public int ProcTheme { get; set; }

        public bool ProcBoss { get; set; }

        /// <summary>The Game Mode dialog's last choice (0 classic, 1 procedural campaign, 2 random map).</summary>
        public int LastGameMode { get; set; }

        /// <summary>The main menu's and the About box's animations play (their pause button turns them off).</summary>
        public bool MenuAnimations { get; set; } = true;

        /// <summary>The key layout chosen last (a key per action); the title screen and new profiles start with it.</summary>
        public int[]? KeyBindings { get; set; }

        public GameSettings Normalized()
        {
            ProcSize = Math.Clamp(ProcSize, 0, 4);
            ProcLength = ProcLength is 0 or 3 or 5 or 10 ? ProcLength : 10;
            ProcDifficulty = Math.Clamp(ProcDifficulty, 0, 4);
            ProcTheme = Math.Clamp(ProcTheme, 0, 10);
            LastGameMode = Math.Clamp(LastGameMode, 0, 2);
            if (KeyBindings is { } keys && (keys.Length != Core.Game.GameInput.ActionCount || keys.Any(k => k is < 0 or >= 62) || keys.Distinct().Count() != keys.Length))
            {
                KeyBindings = null;
            }
            UpdateSpeed = Math.Clamp(UpdateSpeed, 5, 30);
            Resolution = Math.Clamp(Resolution, 0, 2);
            PlayfieldWidth = PlayfieldWidth == 40 ? 40 : 60;
            Difficulty = Math.Clamp(Difficulty, 0, 4);
            SoundVolume = Math.Clamp(SoundVolume, 0, 1);
            MusicVolume = Math.Clamp(MusicVolume, 0, 1);
            PanelScale = PanelScale <= 0 ? 0 : Math.Clamp(PanelScale, 0.75, 4);
            return this;
        }
    }

    internal sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

    internal sealed record PanelPlacement(bool Docked, double Left, double Top, double Width, double Height);

    internal sealed class SettingsService
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly string path;

        public SettingsService(string? path = null)
        {
            this.path = path ?? AppPaths.SettingsFile;
        }

        public GameSettings Current { get; private set; } = new();

        public void Load()
        {
            try
            {
                if (File.Exists(path))
                {
                    Current = (JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path), Json) ?? new GameSettings()).Normalized();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not read {Path}; using defaults", path);
                Current = new GameSettings();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(Current.Normalized(), Json));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not write {Path}", path);
            }
        }
    }
}
