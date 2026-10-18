using Serilog;
using Thexder.Core.Assets;
using Thexder.Core.Audio;
using Thexder.Core.Engine;
using Thexder.Core.Game;
using Thexder.Feature.Audio;
using Thexder.Feature.Video;
using Thexder.Services;
using Thexder.Utils;

namespace Thexder.Feature
{
    internal interface IGameView
    {
        void ShowTitle(IReadOnlyList<string> players, string lastPlayer);

        void HideTitle();

        void ShowScoreButtons(bool show);

        void MovieStarted();

        void MovieStopped();

        string? AskCodeword();

        bool AskYesNo(string text, string caption);

        void ShowMessage(string text, string caption);

        /// <summary>Game Select: one row per difficulty (its name, and the world and level reached).</summary>
        int? ChooseGame(IReadOnlyList<(string Title, string Detail)> rows, int preselect);

        int ChooseDifficulty(IReadOnlyList<string> names, int current);

        void ShowHallOfFame(int world, int level);

        void ShowDemoCard(byte[] bgra, int width, int height);

        /// <summary>After logging in: the Game Mode dialog (and the setup of a generated game).</summary>
        GameModeChoice ChooseGameMode();

        /// <summary>A generated game is over: the result, then what next.</summary>
        void ProceduralFinished();

        void HideDemoCard();

        void Quit();
    }

    /// <summary>
    /// Runs the game for the application: the fixed game tick, palette cycling, keyboard mapping, movies and music, and
    /// the front buffer the window presents.
    /// </summary>
    internal sealed class GameController : IGameHost, IDisposable
    {
        public static readonly int[] KeyTable =
        {
            0x08, 0x09, 0x0c, 0x0d, 0x13, 0x20, 0x29, 0x2b, 0x2c, 0x2d, 0x2e, 0x2f,
            0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
            0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x4b, 0x4c, 0x4d,
            0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a,
            0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
        };

        private readonly IGameView view;
        private readonly AudioService? audio;
        private readonly SoundMixer mixer;
        private readonly PeResourceFile exe;
        private readonly Dictionary<ArtResolution, GameArt> artByResolution = new();
        private DemoLoop? demoLoop;
        private long lastUpdate = -1;
        private long lastSmoothRender = -1;
        private long accumulated;

        public GameController(GameSettings settings, string dataDirectory, AudioService? audio, IGameView view)
        {
            Settings = settings;
            DataDirectory = dataDirectory;
            this.audio = audio;
            this.view = view;
            mixer = audio?.Mixer ?? new SoundMixer();
            exe = PeResourceFile.Open(Path.Combine(dataDirectory, "THEXDER.EXE"));
            Strings = exe.ReadStringTable();

            var sounds = new SoundBoard(mixer, SoundBoard.ReadTable(exe), LoadWave);
            var art = Art((ArtResolution)settings.Resolution);
            Game = new ThexderGame(art, sounds, new GameRandom((uint)Environment.TickCount), this, dataDirectory)
            {
                SaveDirectory = AppPaths.SaveDirectory,
                ExeStrings = Strings,
            };
            Game.ResizeView(settings.PlayfieldWidth);
            Game.FramePresented += OnFramePresented;
            Game.TitleScreenRequested += () =>
            {
                Settings.KeyBindings?.CopyTo(Game.KeyBindings, 0); // the chosen layout, for new profiles
                view.ShowTitle(Game.KnownPlayers().ToList(), Settings.PlayerName);
            };
            Game.CodewordRequested += OnCodewordRequested;
            Net = new NetGame(Game);
            Cycler = new PaletteCycler(Game.Palette);
            Cycler.Reset();
            Music = new MusicPlayer(audio?.Music, dataDirectory);
            Movie = new MoviePlayer(mixer);
            Movie.Finished += OnMovieFinished;
            ApplySettings();
        }

        public GameSettings Settings { get; }

        public string DataDirectory { get; }

        public IReadOnlyDictionary<int, string> Strings { get; }

        public ThexderGame Game { get; }

        public PaletteCycler Cycler { get; }

        public MusicPlayer Music { get; }

        /// <summary>The network session (File > Connect).</summary>
        public NetGame Net { get; }

        public MoviePlayer Movie { get; }

        /// <summary>
        /// The last presented frame (indexed) and its shake offset; the game draws over Screen afterwards.
        /// </summary>
        public FrameBuffer Front { get; private set; } = new(1, 1);

        public (int X, int Y) FrontShake { get; private set; }

        /// <summary>Incremented whenever a new frame was presented.</summary>
        public int FrameVersion { get; private set; }

        /// <summary>Incremented whenever the palette changed (cycling).</summary>
        public int PaletteVersion { get; private set; }

        public bool Minimized { get; set; }

        /// <summary>Set while a modal dialog is open: no game ticks, no colour cycling, no catching up afterwards.</summary>
        public bool Held { get; set; }

        /// <summary>The playfield drawn between game frames (Smooth Motion), playfield-sized.</summary>
        public FrameBuffer SmoothFrame { get; private set; } = new(1, 1);

        /// <summary>
        /// Smooth Motion: draws the playfield as it stands between the last two game frames, by the time elapsed in the
        /// current tick.
        /// </summary>
        public bool RenderSmooth()
        {
            var g = Game;
            if (!Settings.SmoothMotion || Minimized || !g.SceneValid || !g.SessionActive || g.Player.PlayState != 0 || g.MoviePlaying)
            {
                return false;
            }
            if (SmoothFrame.Width != g.ViewWidthPx || SmoothFrame.Height != g.ViewHeightPx)
            {
                SmoothFrame = new FrameBuffer(g.ViewWidthPx, g.ViewHeightPx);
            }
            double alpha = Held || g.PauseCount > 0 ? 1 : accumulated / (double)TickMs;
            long now = lastUpdate;
            double elapsed = lastSmoothRender < 0 ? 0 : now - lastSmoothRender;
            lastSmoothRender = now;
            return g.RenderSmooth(alpha, SmoothFrame, elapsed);
        }

        public int TickMs => Math.Clamp(1000 / Math.Max(1, Settings.UpdateSpeed), 33, 200);

        /// <summary>The "/X" kiosk demo loop runs instead of the game.</summary>
        public bool Kiosk => demoLoop != null;

        public void Start() => Game.Start();

        public void StartKiosk() => demoLoop = new DemoLoop(Game, DataDirectory, view);

        /// <summary>Called on every rendered frame of the window: advances the game by whole ticks.</summary>
        public void Update(long nowMs)
        {
            if (lastUpdate < 0)
            {
                lastUpdate = nowMs;
            }
            long dt = nowMs - lastUpdate;
            lastUpdate = nowMs;
            if (Minimized || Held)
            {
                accumulated = 0;
                return;
            }
            Net.Poll();
            demoLoop?.Update(nowMs);
            int tick = TickMs;
            accumulated = Math.Min(accumulated + dt, tick * 10L);
            int ran = 0;
            while (accumulated >= tick && ran < 2)
            {
                accumulated -= tick;
                if (Cycler.Update(nowMs))
                {
                    PaletteVersion++;
                }
                Game.GameFrame();
                ran++;
            }
            if (Movie.IsPlaying)
            {
                Movie.Update();
            }
        }

        /// <summary>A key went down or up (Windows virtual-key code, keypad mapped as with NumLock off).</summary>
        public void Key(int vk, bool down)
        {
            vk = NumLockOff(vk);
            Game.Input.SetKey(vk, down);
            for (int a = 0; a < GameInput.ActionCount; a++)
            {
                int index = Game.KeyBindings[a];
                if (index >= 0 && index < KeyTable.Length && KeyTable[index] == vk)
                {
                    Game.Input.SetAction((GameAction)a, down);
                }
            }
            Game.Input.CheatCombo = Game.Input.IsKeyHeld('T') && Game.Input.IsKeyHeld('X');
            if (down)
            {
                Game.Touch();
            }
        }

        public static int NumLockOff(int vk) => vk switch
        {
            0x60 => 0x2d,
            0x61 => 0x23,
            0x62 => 0x28,
            0x63 => 0x22,
            0x64 => 0x25,
            0x65 => 0x0c,
            0x66 => 0x27,
            0x67 => 0x24,
            0x68 => 0x26,
            0x69 => 0x21,
            0x6e => 0x2e,
            _ => vk,
        };

        public string KeyName(int index) => Strings.TryGetValue(500 + index, out var s) ? s : $"Key {index}";

        private void OnCodewordRequested()
        {
            Game.Input.ReleaseAll();
            string? word = view.AskCodeword();
            if (!string.IsNullOrEmpty(word))
            {
                Game.ApplyCodeword(word);
            }
        }

        private void OnFramePresented()
        {
            var screen = Game.Screen;
            if (Front.Width != screen.Width || Front.Height != screen.Height)
            {
                Front = new FrameBuffer(screen.Width, screen.Height);
            }
            Array.Copy(screen.Pixels, Front.Pixels, screen.Pixels.Length);
            FrontShake = Game.ShakeOffset;
            FrameVersion++;
        }

        public void ApplySettings()
        {
            Game.RecordScene = Settings.SmoothMotion;
            Game.SmartAim = Settings.SmartAim;
            Game.Sounds.SoundEnabled = Settings.Sound;
            Game.Sounds.SpeechEnabled = Settings.Speech;
            Game.ScrollingBackground = Settings.ScrollingBackground;
            mixer.EffectsVolume = (float)Settings.SoundVolume;
            mixer.StreamVolume = (float)Settings.SoundVolume;
            Music.SetVolume((float)Settings.MusicVolume);
            if (Music.Enabled != Settings.Music)
            {
                Music.Enabled = Settings.Music;
                if (Settings.Music)
                {
                    Music.Resume();
                }
                else
                {
                    Music.Stop();
                }
            }
        }

        public void SetResolution(ArtResolution resolution)
        {
            Settings.Resolution = (int)resolution;
            Game.ApplyArt(Art(resolution));
            Cycler.Reset();
            RedrawStatic();
        }

        public void SetPlayfieldWidth(int cells)
        {
            Settings.PlayfieldWidth = cells;
            Game.ResizeView(cells);
            RedrawStatic();
        }

        /// <summary>Redraws screens that are not refreshed by game ticks (title, score screen).</summary>
        public void RedrawStatic()
        {
            if (!Game.SessionActive && !Game.MoviePlaying)
            {
                Game.DrawTitleScreen();
            }
            else if (Game.Player.PlayState == 3)
            {
                Game.RedrawScoreScreen();
            }
        }

        public bool HasResolution(ArtResolution resolution) => ResourceLibrary.Exists(DataDirectory, resolution);

        private GameArt Art(ArtResolution resolution)
        {
            if (!HasResolution(resolution))
            {
                resolution = Enum.GetValues<ArtResolution>().First(HasResolution);
            }
            if (!artByResolution.TryGetValue(resolution, out var art))
            {
                art = new GameArt(ResourceLibrary.Open(DataDirectory, resolution));
                artByResolution[resolution] = art;
            }
            return art;
        }

        private WaveFile? LoadWave(string name)
        {
            string path = Path.Combine(DataDirectory, "WAVE", name + ".WAV");
            try
            {
                return File.Exists(path) ? WaveFile.Load(path) : null;
            }
            catch (InvalidDataException ex)
            {
                Log.Warning(ex, "Bad sound {Name}", name);
                return null;
            }
        }

        public void PlayMovie(string name)
        {
            Music.Stop();
            if (Movie.Start(Path.Combine(DataDirectory, name)))
            {
                view.MovieStarted();
            }
            else
            {
                Game.Advance();
            }
        }

        public void StopMovie()
        {
            Movie.Stop();
            view.MovieStopped();
            Music.Resume();
        }

        private void OnMovieFinished()
        {
            view.MovieStopped();
            if (Game.MoviePlaying)
            {
                Game.Advance();
            }
        }

        public void StartLevelMusic(int world, int level)
        {
            if (world == 0 || world > 10)
            {
                world = Game.Random.Next() % 10 + 1;
                level = Game.Random.Next() % 5 + 1;
            }
            Music.PlayLevel(world, level);
        }

        public void StopMusic() => Music.Stop();

        public void ShowScoreButtons(bool show) => view.ShowScoreButtons(show);

        public string? AskCodeword() => view.AskCodeword();

        public bool AskYesNo(string text, string caption) => view.AskYesNo(text, caption);

        public void ShowMessage(string text, string caption) => view.ShowMessage(text, caption);

        public int? ChooseGame(IReadOnlyList<(int World, int Level)> progress, int lastDifficulty)
        {
            var rows = progress.Select((p, d) => (Game.DifficultyName(d), $"World {p.World} · Level {p.Level}")).ToList();
            return view.ChooseGame(rows, lastDifficulty);
        }

        public int ChooseDifficulty(int current) =>
            view.ChooseDifficulty(Enumerable.Range(0, 5).Select(Game.DifficultyName).ToList(), current);

        public void ShowHallOfFame(int world, int level) => view.ShowHallOfFame(world, level);

        public void Quit() => view.Quit();

        public GameModeChoice ChooseGameMode()
        {
            Game.Input.ReleaseAll();
            return view.ChooseGameMode();
        }

        public void ProceduralFinished() => view.ProceduralFinished();

        /// <summary>What the level generator learned from the classic levels (built on first use).</summary>
        public Procedural.ClassicLibrary Library => Procedural.ClassicLibrary.For(DataDirectory);

        public void Login(string name)
        {
            view.HideTitle();
            Settings.PlayerName = name;
            Game.Login(name);
        }

        public void Dispose()
        {
            Net.Dispose();
            Movie.Dispose();
            Music.Stop();
            foreach (var art in artByResolution.Values)
            {
                art.Dispose();
            }
            exe.Dispose();
        }
    }
}
