using System.Windows;
using Thexder.Core.Game;
using Thexder.Feature;
using Thexder.Feature.Procedural;

namespace Thexder.UI
{
    // The Game Mode dialog after logging in, the setup of generated games and what follows a finished one.
    public partial class MainWindow
    {
        GameModeChoice IGameView.ChooseGameMode()
        {
            if (Dialogs.Preview != null)
            {
                return GameModeChoice.Classic; // UI script previews: straight to the campaign, as before
            }
            var lib = controller.Library;
            string player = Game.PlayerName;
            while (true)
            {
                var savedCampaign = ProcGame.Load(lib, Game.SaveDirectory, player, ProcKind.Campaign, Game.PlanetName);
                var (w, l) = Game.Progress[Math.Clamp(Game.LastDifficulty, 0, 4)];
                string classic = w > 1 || l > 1 ? $"World {w} · Level {l}" : "";
                string campaign = savedCampaign != null ? $"World {savedCampaign.Settings.Stage} · Level {savedCampaign.Level}" : "";
                var mode = Dialogs.ChooseGameMode(this, player, classic, campaign, "", (GameModeOption)Settings.LastGameMode);
                if (mode == null)
                {
                    return GameModeChoice.Cancel;
                }
                Settings.LastGameMode = (int)mode.Value;
                SaveSettingsNow();
                if (mode == GameModeOption.Classic)
                {
                    return GameModeChoice.Classic;
                }
                var choice = mode == GameModeOption.Campaign ? SetupCampaign(lib, savedCampaign) : SetupRandomMap(lib);
                if (choice != null)
                {
                    SyncMenus();
                    return choice;
                }
            }
        }

        private GameModeChoice? SetupCampaign(ClassicLibrary lib, ProcGame? saved)
        {
            var defaults = new ProcSave
            {
                Kind = ProcKind.Campaign,
                Seed = ProcGame.NewSeed(),
                Size = (MapSize)Settings.ProcSize,
                Length = Settings.ProcLength,
                Difficulty = Settings.ProcDifficulty,
            };
            string summary = "";
            if (saved != null)
            {
                var s = saved.Settings;
                string arc = s.Length > 0 ? $"world {s.Stage} of {s.Length}" : $"world {s.Stage} (endless)";
                summary = $"{Game.PlanetName(saved.World)}, level {s.Level}, {arc} · {MapSizes.Name(s.Size)} maps · {Game.DifficultyName(s.Difficulty)} · seed {s.Seed} · score {s.TotalScore}";
            }
            var setup = Dialogs.CampaignSetup(this, saved?.Settings, summary, defaults, Enumerable.Range(0, 5).Select(Game.DifficultyName).ToList());
            if (setup == null)
            {
                return null;
            }
            ProcGame game;
            if (setup.Resume && saved != null)
            {
                game = saved;
                game.RestoreInto(Game);
            }
            else
            {
                Settings.ProcSize = (int)setup.Settings.Size;
                Settings.ProcLength = setup.Settings.Length;
                Settings.ProcDifficulty = setup.Settings.Difficulty;
                SaveSettingsNow();
                game = ProcGame.Create(setup.Settings, lib, Game.SaveDirectory, Game.PlayerName, Game.PlanetName);
            }
            return new GameModeChoice(GameModeKind.Procedural, game, setup.Resume);
        }

        private GameModeChoice? SetupRandomMap(ClassicLibrary lib)
        {
            var defaults = new ProcSave
            {
                Kind = ProcKind.Single,
                Seed = ProcGame.NewSeed(),
                Size = (MapSize)Settings.ProcSize,
                Difficulty = Settings.ProcDifficulty,
                Theme = Settings.ProcTheme,
                Boss = Settings.ProcBoss,
            };
            var planets = Enumerable.Range(1, 10).Select(Game.PlanetName).ToList();
            var settings = Dialogs.RandomMapSetup(this, defaults, Enumerable.Range(0, 5).Select(Game.DifficultyName).ToList(), planets, s => PreviewMap(lib, s));
            if (settings == null)
            {
                return null;
            }
            Settings.ProcSize = (int)settings.Size;
            Settings.ProcDifficulty = settings.Difficulty;
            Settings.ProcBoss = settings.Boss;
            SaveSettingsNow();
            return new GameModeChoice(GameModeKind.Procedural, ProcGame.Create(settings, lib, Game.SaveDirectory, Game.PlayerName, Game.PlanetName));
        }

        // Builds the map on a worker thread (the generator only reads the library), then pictures it here.
        private async Task<MapPicture?> PreviewMap(ClassicLibrary lib, ProcSave settings)
        {
            var game = ProcGame.Create(settings, lib, Game.SaveDirectory, Game.PlayerName, Game.PlanetName);
            LevelData level;
            try
            {
                level = await Task.Run(game.BuildLevel);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Map preview failed");
                return null;
            }
            var colors = MapPreview.TileColors(Game.Art, level.TileSheet);
            var pixels = MapPreview.Render(level, colors);
            int items = 0;
            for (int i = 0; i < level.Width * level.Height; i++)
            {
                items += (level.Cells[i * 4 + 3] & 0x3f) != 0 ? 1 : 0;
            }
            string caption = $"{Game.PlanetName(game.World)} · {level.Width} × {level.Height} cells · {level.Enemies.Count} enemies · {items} pickups";
            return new MapPicture(pixels, level.Width, level.Height, caption);
        }

        void IGameView.ProceduralFinished()
        {
            if (Game.Procedural is not ProcGame game)
            {
                Game.ChooseMode();
                return;
            }
            var s = game.Settings;
            if (s.Kind == ProcKind.Single)
            {
                if (Dialogs.MapComplete(this, $"Map complete!\nScore {s.TotalScore}."))
                {
                    var next = new ProcSave { Kind = ProcKind.Single, Seed = ProcGame.NewSeed(), Size = s.Size, Difficulty = s.Difficulty, Boss = s.Boss };
                    next.Theme = Settings.ProcTheme == 0 ? (int)(next.Seed % 10) + 1 : Settings.ProcTheme;
                    Game.StartProcedural(ProcGame.Create(next, controller.Library, Game.SaveDirectory, Game.PlayerName, Game.PlanetName), resume: false);
                    SyncMenus();
                    return;
                }
            }
            else
            {
                Dialogs.Message(this, $"You have completed your mission!\n{s.LevelsDone} levels, total score {s.TotalScore}.", "Procedural Campaign");
            }
            Game.PauseCount++;
            Game.ChooseMode();
            SyncMenus();
        }

        private void OnGameMode(object sender, RoutedEventArgs e)
        {
            Game.Input.ReleaseAll();
            Game.MenuChangeMode();
            SyncMenus();
        }

        private void SaveSettingsNow() => settingsService.Save();
    }
}
