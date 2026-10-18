using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Serilog;
using Thexder.Core.Assets;
using Thexder.Core.Engine;
using Thexder.Core.Game;
using Thexder.Feature;
using Thexder.Feature.Rendering;
using Thexder.Services;

namespace Thexder.UI
{
    public partial class MainWindow : IGameView
    {
        private readonly SettingsService settingsService;
        private readonly AudioService? audio;
        private readonly GameController controller;
        private readonly GameOptions options;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly ScreenPresenter playfieldPresenter = new();
        private readonly ScreenPresenter hudPresenter = new();
        private readonly ScreenPresenter mapPresenter = new();
        private readonly ScreenPresenter weaponsPresenter = new();
        private readonly ScreenPresenter equipmentPresenter = new();
        private readonly ScreenPresenter enemyPresenter = new();
        private readonly ScreenPresenter moviePresenter = new();
        private readonly WindowDock dock;
        private PanelWindow? weaponsWindow;
        private PanelWindow? equipmentWindow;
        private PanelWindow? enemyWindow;
        private PanelWindow? mapWindow;
        private int shownFrame = -1;
        private bool smoothShown;
        private int shownPalette = -1;
        private bool closingConfirmed;
        private WindowState stateBeforeFullscreen;
        private WindowStyle styleBeforeFullscreen;

        internal MainWindow(string dataDir, GameOptions options)
        {
            InitializeComponent();
            this.options = options;
            ShowActivated = options.UiScriptDir == null; // Scripted runs must not take the focus from the user
            // Scripted runs keep their own settings next to their output, never the player's.
            settingsService = new SettingsService(options.UiScriptDir != null ? Path.Combine(options.UiScriptDir, "settings.json") : null);
            settingsService.Load();
            var settings = settingsService.Current;
            if (options.Mute)
            {
                settings.Sound = false;
                settings.Music = false;
            }
            try
            {
                audio = new AudioService();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Audio initialisation failed");
            }
            controller = new GameController(settings, dataDir, audio, this);
            if (options.UiScriptDir != null)
            {
                controller.Game.SaveDirectory = Path.Combine(options.UiScriptDir, "saves"); // And their own saves
            }
            TitleMenu.PlayRequested += Login;
            TitleMenu.ControlsRequested += () => OnHelp(this, new RoutedEventArgs());
            TitleMenu.RemapRequested += () => OnRemapKeys(this, new RoutedEventArgs());
            TitleMenu.AboutRequested += () => OnAbout(this, new RoutedEventArgs());
            TitleMenu.ExitRequested += Close;
            TitleMenu.DeleteRequested += DeleteProfile;
            TitleMenu.Animated = settings.MenuAnimations;
            TitleMenu.AnimatedChanged += on =>
            {
                Settings.MenuAnimations = on;
                SaveSettingsNow();
            };
            dock = new WindowDock(this);
            if (settings.PanelScale > 0)
            {
                dock.PanelScale = settings.PanelScale;
            }
            dock.MapHeight = settings.MapHeight;
            bool placed = RestorePlacement(settings.Window);
            SyncMenus();
            LayoutStage();
            if (!placed)
            {
                FitToWorkArea();
            }

            InitNetwork();
            Loaded += OnLoaded;
            Closing += OnClosing;
            if (Application.Current != null)
            {
                Application.Current.Activated += OnApplicationActivated;
                Application.Current.Deactivated += OnApplicationDeactivated;
            }
            StateChanged += OnStateChanged;
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewKeyUp += OnPreviewKeyUp;
        }

        private GameSettings Settings => settingsService.Current;

        private void OnApplicationActivated(object? sender, EventArgs e) => controller.Game.Input.Active = true;

        private void OnApplicationDeactivated(object? sender, EventArgs e)
        {
            controller.Game.Input.ReleaseAll();
            controller.Game.Input.Active = false;
        }

        private ThexderGame Game => controller.Game;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CreatePanels();
            CompositionTarget.Rendering += OnRendering;
            if (options.Fullscreen || Settings.Fullscreen)
            {
                SetFullscreen(true);
            }
            if (options.Kiosk)
            {
                controller.StartKiosk();
                SyncMenus();
                if (options.UiScriptDir != null)
                {
                    StartUiScript(options.UiScriptDir, options.UiScript ?? "");
                }
                return;
            }
            if (options.World != null)
            {
                // Developer start: one level outside the campaign, no title screen.
                string file = LevelFile.FileName(options.World.Value, options.Level ?? 1);
                Game.IntroPending = false;
                Game.SessionActive = true;
                Game.PlayerName = "Test";
                Game.MenuLoadLevel(Path.Combine(controller.DataDirectory, file), Settings.Difficulty);
                Game.World = options.World.Value;
                Game.Level = options.Level ?? 1;
                if (options.UiScriptDir != null)
                {
                    StartUiScript(options.UiScriptDir, options.UiScript ?? "");
                }
                return;
            }
            controller.Start();
            if (options.UiScriptDir != null)
            {
                StartUiScript(options.UiScriptDir, options.UiScript ?? "");
            }
        }

        // ---- Frame loop ----------------------------------------------------------------------------------

        private void OnRendering(object? sender, EventArgs e)
        {
            double start = clock.Elapsed.TotalMilliseconds;
            RenderFrame();
            TraceSmooth(start, e is RenderingEventArgs r ? r.RenderingTime.TotalMilliseconds : 0);
        }

        private void RenderFrame()
        {
            if (IsActive && Keyboard.FocusedElement == null)
            {
                // Never leave the active window without keyboard focus: its keys would go nowhere
                if (TitleMenu.IsOpen)
                {
                    TitleMenu.FocusMenu();
                }
                else
                {
                    FocusGame();
                }
            }
            controller.Held = Dialogs.OpenCount > 0;
            controller.Update(clock.ElapsedMilliseconds);
            if (TitleMenu.IsOpen)
            {
                // The stage is hidden under the menu: nothing to present. A dialog over the menu holds its scene.
                bool held = Dialogs.OpenCount > 0;
                if (TitleMenu.Paused != held)
                {
                    TitleMenu.Paused = held;
                }
                return;
            }
            if (controller.Movie.IsPlaying || MovieOverlay.Visibility == Visibility.Visible)
            {
                if (controller.Movie.IsPlaying && controller.Movie.Width > 0)
                {
                    // The bitmap is reused between movies of the same size, and MovieStarted clears the image.
                    moviePresenter.PresentBgra(controller.Movie.Pixels, controller.Movie.Width, controller.Movie.Height);
                    if (MovieImage.Source != moviePresenter.Bitmap)
                    {
                        MovieImage.Source = moviePresenter.Bitmap;
                    }
                }
            }
            // Smooth Motion: the playfield is redrawn on every display frame, between the game's frames.
            smoothShown = controller.RenderSmooth();
            if (smoothShown && playfieldPresenter.Present(controller.SmoothFrame, new Core.Engine.Rect(0, 0, Game.ViewWidthPx, Game.ViewHeightPx), Game.Palette))
            {
                Playfield.Source = playfieldPresenter.Bitmap;
            }
            if (shownFrame == controller.FrameVersion && shownPalette == controller.PaletteVersion)
            {
                return;
            }
            bool newFrame = shownFrame != controller.FrameVersion;
            shownFrame = controller.FrameVersion;
            shownPalette = controller.PaletteVersion;
            Present(newFrame);
        }

        private void Present(bool newFrame)
        {
            var front = controller.Front;
            var palette = Game.Palette;
            int playH = Game.ViewHeightPx;
            if (front.Height < playH)
            {
                return;
            }
            if (!smoothShown && playfieldPresenter.Present(front, new Core.Engine.Rect(0, 0, front.Width, playH), palette))
            {
                Playfield.Source = playfieldPresenter.Bitmap;
            }
            if (hudPresenter.Present(front, new Core.Engine.Rect(0, playH, 320, ThexderGame.HudHeight), palette))
            {
                Hud.Source = hudPresenter.Bitmap;
            }
            Shake.X = controller.FrontShake.X;
            Shake.Y = controller.FrontShake.Y;

            if (mapWindow is { IsVisible: true } && mapPresenter.Present(Game.MapSurface, Game.MapViewRect, palette))
            {
                mapWindow.SetPicture(mapPresenter.Bitmap);
            }
            if (newFrame && Game.SessionActive)
            {
                if (weaponsWindow is { IsVisible: true })
                {
                    Game.DrawWeaponsPanel();
                }
                if (equipmentWindow is { IsVisible: true })
                {
                    Game.DrawEquipmentPanel();
                }
                if (enemyWindow is { IsVisible: true })
                {
                    Game.DrawEnemyPanel();
                }
            }
            if (weaponsWindow is { IsVisible: true } && weaponsPresenter.Present(Game.WeaponsPanel, palette))
            {
                weaponsWindow.SetPicture(weaponsPresenter.Bitmap);
            }
            if (equipmentWindow is { IsVisible: true } && equipmentPresenter.Present(Game.EquipmentPanel, palette))
            {
                equipmentWindow.SetPicture(equipmentPresenter.Bitmap);
            }
            if (enemyWindow is { IsVisible: true } && enemyPresenter.Present(Game.EnemyPanel, Game.EnemyPanelArea, palette))
            {
                enemyWindow.SetPicture(enemyPresenter.Bitmap);
            }
            mapWindow?.SetTitle(Game.SessionActive ? Game.MapTitle : "Map of Planet");
            if (Game.SessionActive)
            {
                TitleBar.Title = Game.NetActive ? "Thexder - Network game" : $"Thexder - {Game.LevelLabel}";
            }
        }

        /// <summary>Sizes the stage in game pixels: the playfield, then the 320 x 64 HUD strip scaled up to 2x.</summary>
        private void LayoutStage()
        {
            int w = Game.ViewWidthPx, h = Game.ViewHeightPx;
            double hud = HudScale;
            Stage.Width = w;
            PlayfieldRow.Height = new GridLength(h);
            HudRow.Height = new GridLength(64 * hud);
            Hud.Width = 320 * hud;
            Hud.HorizontalAlignment = HorizontalAlignment.Center;
            Stage.Height = h + 64 * hud;
            shownFrame = -1;
        }

        private double HudScale => Math.Min(2.0, Game.ViewWidthPx / 320.0);

        /// <summary>Sizes the main window and the docked side windows to fit the monitor the window is on.</summary>
        private void FitToWorkArea() => ApplyLayout(MonitorArea.Of(this).Work);

        /// <summary>The Standard Configuration for a work area: everything docked, as large as fits.</summary>
        private void ApplyLayout(System.Windows.Rect work)
        {
            const double chromeHeight = 32 + 26; // Title bar and menu
            if (WindowStyle == WindowStyle.None)
            {
                SetFullscreen(false);
            }
            dock.ApplyLayout(work, Game.ViewWidthPx, Game.ViewHeightPx + 64 * HudScale, chromeHeight);
            UpdatePanels();
        }

        private void OnLayoutFit(object sender, RoutedEventArgs e) => FitToWorkArea();

        /// <summary>Window > Layout > W × H: the layout a monitor of that resolution gets, centred on this one.</summary>
        private void OnLayoutPreset(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: string tag })
            {
                var (w, h) = ParsePreset(tag);
                if (w > 0)
                {
                    ApplyLayout(MonitorArea.Of(this).WorkFor(w, h));
                }
            }
        }

        private void OnLayoutMenuOpened(object sender, RoutedEventArgs e)
        {
            var monitor = MonitorArea.Of(this);
            foreach (var item in MenuLayout.Items.OfType<MenuItem>())
            {
                if (item.Tag is not string tag)
                {
                    continue;
                }
                var (w, h) = ParsePreset(tag);
                if (w == 0)
                {
                    continue;
                }
                item.IsEnabled = monitor.Holds(w, h);
                bool current = w == monitor.PixelWidth && h == monitor.PixelHeight;
                item.Header = $"{w} × {h}{(current ? "  (this monitor)" : "")}";
            }
        }

        private static (int W, int H) ParsePreset(string tag)
        {
            string[] parts = tag.Split('x');
            return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) ? (w, h) : (0, 0);
        }

        // ---- Side windows --------------------------------------------------------------------------------

        private void CreatePanels()
        {
            weaponsWindow = NewPanel(PanelSlot.Weapons, "Weapons", ThexderGame.PanelW, ThexderGame.PanelH, new Thickness(0));
            equipmentWindow = NewPanel(PanelSlot.Equipment, "Equipment", ThexderGame.PanelW, ThexderGame.PanelH, new Thickness(0));
            enemyWindow = NewPanel(PanelSlot.Enemy, "Enemy Status", ThexderGame.EnemyPanelSize, ThexderGame.EnemyPanelSize, new Thickness(10));
            mapWindow = NewPanel(PanelSlot.Map, "Map of Planet", CellMap.ClassicWidth, CellMap.ClassicHeight, new Thickness(0));
            weaponsWindow.LeftClick += (x, y, _) => Game.ClickWeaponsPanel(x, y);
            equipmentWindow.LeftClick += (x, y, dbl) => Game.ClickEquipmentPanel(x, y, dbl);
            enemyWindow.LeftClick += (_, _, _) => Game.ClickEnemyPanel(nextType: true);
            enemyWindow.RightClick += (_, _) => Game.ClickEnemyPanel(nextType: false);
            enemyWindow.ToolTip = "Click: next enemy. Right click: statistics.";
            dock.Arrange();
            UpdatePanels();
            Activate();
        }

        private PanelWindow NewPanel(PanelSlot slot, string title, int w, int h, Thickness margin)
        {
            var panel = new PanelWindow(title, w, h, margin) { Owner = this };
            // Keys pressed while a side window has the focus still play the game.
            panel.PreviewKeyDown += OnPreviewKeyDown;
            panel.PreviewKeyUp += OnPreviewKeyUp;
            PanelPlacement? saved = null;
            Settings.Panels?.TryGetValue(slot.ToString(), out saved);
            dock.Add(slot, panel, saved?.Docked ?? true);
            dock.Restore(slot, saved);
            panel.HiddenByUser += () =>
            {
                switch (slot)
                {
                    case PanelSlot.Weapons: Settings.ShowWeapons = false; break;
                    case PanelSlot.Equipment: Settings.ShowEquipment = false; break;
                    case PanelSlot.Enemy: Settings.ShowEnemyStatus = false; break;
                    default: Settings.ShowMap = false; break;
                }
                SyncMenus();
                UpdatePanels();
            };
            return panel;
        }

        /// <summary>Shows the side windows that are switched on; docked ones hide while maximized or full screen.</summary>
        private void UpdatePanels()
        {
            void Show(PanelWindow? panel, PanelSlot slot, bool on)
            {
                if (panel == null)
                {
                    return;
                }
                bool visible = on && WindowState != WindowState.Minimized && (dock.CanDock || !dock.IsDocked(slot));
                if (visible && !panel.IsVisible)
                {
                    panel.Show();
                }
                else if (!visible && panel.IsVisible)
                {
                    panel.Hide();
                }
            }
            Show(weaponsWindow, PanelSlot.Weapons, Settings.ShowWeapons);
            Show(equipmentWindow, PanelSlot.Equipment, Settings.ShowEquipment);
            Show(enemyWindow, PanelSlot.Enemy, Settings.ShowEnemyStatus);
            Show(mapWindow, PanelSlot.Map, Settings.ShowMap);
            dock.Arrange();
            shownFrame = -1;
        }

        /// <summary>Window > Standard Configuration: all side windows docked, sizes reset to fit this monitor.</summary>
        private void OnStandardConfiguration(object sender, RoutedEventArgs e) => FitToWorkArea();

        // ---- Keyboard and mouse --------------------------------------------------------------------------

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (TitleMenu.IsOpen)
            {
                // The main menu keeps its keys; only the window keys work over it.
                if (e.Key == Key.F1)
                {
                    OnHelp(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (e.Key == Key.F11)
                {
                    SetFullscreen(WindowStyle != WindowStyle.None);
                    e.Handled = true;
                }
                return;
            }
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (controller.Kiosk && key != Key.F11 && key != Key.F3 && !(key == Key.Escape && WindowStyle == WindowStyle.None))
            {
                // Kiosk mode: Esc exits, every other key is ignored.
                if (key == Key.Escape)
                {
                    Close();
                }
                if (e.Key != Key.System)
                {
                    e.Handled = true;
                }
                return;
            }
            switch (key)
            {
                case Key.F11:
                    SetFullscreen(WindowStyle != WindowStyle.None);
                    e.Handled = true;
                    return;
                case Key.Escape when Game.NetActive:
                    Game.NetEscape(); // Network play: transport to a random place for 10 points
                    e.Handled = true;
                    return;
                case Key.Escape when WindowStyle == WindowStyle.None:
                    SetFullscreen(false);
                    e.Handled = true;
                    return;
                case Key.F3:
                    Game.ShowFps = !Game.ShowFps;
                    e.Handled = true;
                    return;
                case Key.F1:
                    OnHelp(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.P when ctrl:
                    OnPause(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.Enter when !e.IsRepeat:
                    if (ScoreOverlay.Visibility == Visibility.Visible)
                    {
                        OnScoreContinue(this, new RoutedEventArgs());
                        e.Handled = true;
                        return;
                    }
                    if (Game.MoviePlaying || Game.Player.PlayState != 0)
                    {
                        Game.Advance();
                    }
                    break;
            }
            if (e.Key == Key.System && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt || e.SystemKey == Key.F10 || e.SystemKey == Key.F4))
            {
                return; // Let the menu bar work
            }
            controller.Key(Dialogs.VirtualKey(e), true);
            e.Handled = true;
        }

        private void OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            controller.Key(Dialogs.VirtualKey(e), false);
        }

        private void OnPlayfieldClick(object sender, MouseButtonEventArgs e)
        {
            Game.Advance();
        }

        private void OnMovieClick(object sender, MouseButtonEventArgs e) => Game.Advance();

        private void Login(string name)
        {
            FocusGame();
            controller.Login(name);
            SyncMenus();
        }

        private void OnScoreContinue(object sender, RoutedEventArgs e) => Game.ScoreContinue();

        private void OnScoreReplay(object sender, RoutedEventArgs e) => Game.ScoreReplay();

        private void SyncMenus()
        {
            MenuShowMap.IsChecked = Settings.ShowMap;
            MenuShowWeapons.IsChecked = Settings.ShowWeapons;
            MenuShowEquipment.IsChecked = Settings.ShowEquipment;
            MenuShowEnemy.IsChecked = Settings.ShowEnemyStatus;
            MenuSound.IsChecked = Settings.Sound;
            MenuMusic.IsChecked = Settings.Music;
            MenuSpeech.IsChecked = Settings.Speech;
            MenuScrolling.IsChecked = Settings.ScrollingBackground;
            MenuSmooth.IsChecked = Settings.SmoothMotion;
            MenuSmartAim.IsChecked = Settings.SmartAim;
            MenuResLow.IsChecked = Settings.Resolution == 0;
            MenuResMedium.IsChecked = Settings.Resolution == 1;
            MenuResHigh.IsChecked = Settings.Resolution == 2;
            MenuResLow.IsEnabled = controller.HasResolution(ArtResolution.Low);
            MenuResMedium.IsEnabled = controller.HasResolution(ArtResolution.Medium);
            MenuResHigh.IsEnabled = controller.HasResolution(ArtResolution.High);
            MenuStandard.IsChecked = Settings.PlayfieldWidth == 40;
            MenuWide.IsChecked = Settings.PlayfieldWidth == 60;
            SoundVolume.Value = Settings.SoundVolume;
            MusicVolume.Value = Settings.MusicVolume;
            bool inGame = Game.SessionActive && !controller.Kiosk && !Game.NetActive;
            MenuConnect.IsEnabled = Game.SessionActive && !controller.Kiosk && !Game.NetActive;
            MenuDisconnect.IsEnabled = Game.NetActive;
            MenuNetComm.IsEnabled = Game.NetActive;
            MenuLoad.IsEnabled = inGame;
            MenuReplay.IsEnabled = inGame;
            MenuRevert.IsEnabled = inGame && Game.Procedural == null;
            MenuNewGame.IsEnabled = inGame;
            MenuGameMode.IsEnabled = inGame;
            MenuPause.IsEnabled = !controller.Kiosk;
            MenuRemapKeys.IsEnabled = !controller.Kiosk;
            MenuHallOfFame.IsEnabled = !controller.Kiosk;
        }

        private void OnLoadLevel(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Thexder Levels (*.thl)|*.thl|All Files (*.*)|*.*",
                InitialDirectory = controller.DataDirectory,
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            int difficulty = ChooseDifficulty(Enumerable.Range(0, 5).Select(Game.DifficultyName).ToList(), Game.Difficulty);
            try
            {
                Game.MenuLoadLevel(dialog.FileName, difficulty);
            }
            catch (InvalidDataException ex)
            {
                ShowMessage(ex.Message, "Thexder");
            }
        }

        private void OnReplay(object sender, RoutedEventArgs e) => Game.MenuReplay();

        private void OnRevert(object sender, RoutedEventArgs e)
        {
            if (!AskYesNo("OK to abandon the current game in progress?", "Thexder"))
            {
                return;
            }
            var choices = new List<(int World, int Difficulty)>();
            var lines = new List<Choice>();
            for (int i = 0; i < Game.WorldStarts.Length; i++)
            {
                if (Game.WorldStarts[i].Weapon != 0)
                {
                    int world = i / 5 + 1, difficulty = i % 5;
                    choices.Add((world, difficulty));
                    lines.Add(new Choice($"World {world} · {Game.PlanetName(world)}", Game.DifficultyName(difficulty)));
                }
            }
            if (lines.Count == 0)
            {
                ShowMessage("No world has been started yet.", "Revert");
                return;
            }
            int? choice = Dialogs.Choose(this, "Revert", "Select a previously played world:", lines, 0);
            if (choice != null)
            {
                Game.MenuRevert(choices[choice.Value].World, choices[choice.Value].Difficulty);
            }
        }

        private void OnNewGame(object sender, RoutedEventArgs e)
        {
            Game.MenuNewGame();
            SyncMenus();
        }

        private void OnExit(object sender, RoutedEventArgs e) => Close();

        private void OnPanelsChanged(object sender, RoutedEventArgs e)
        {
            Settings.ShowMap = MenuShowMap.IsChecked;
            Settings.ShowWeapons = MenuShowWeapons.IsChecked;
            Settings.ShowEquipment = MenuShowEquipment.IsChecked;
            Settings.ShowEnemyStatus = MenuShowEnemy.IsChecked;
            UpdatePanels();
        }

        private void OnFullscreen(object sender, RoutedEventArgs e) => SetFullscreen(MenuFullscreen.IsChecked);

        private void SetFullscreen(bool on)
        {
            if (on == (WindowStyle == WindowStyle.None))
            {
                return;
            }
            if (on)
            {
                stateBeforeFullscreen = WindowState;
                styleBeforeFullscreen = WindowStyle;
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                TitleBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                WindowStyle = styleBeforeFullscreen == WindowStyle.None ? WindowStyle.SingleBorderWindow : styleBeforeFullscreen;
                WindowState = stateBeforeFullscreen;
                TitleBar.Visibility = Visibility.Visible;
            }
            MenuFullscreen.IsChecked = on;
            Settings.Fullscreen = on;
            UpdatePanels();
        }

        private void OnPause(object sender, RoutedEventArgs e)
        {
            Game.Input.ReleaseAll();
            Dialogs.Message(this, "Thexder game paused.\nClick OK to continue.", "Thexder");
        }

        private void OnAudioChanged(object sender, RoutedEventArgs e)
        {
            Settings.Sound = MenuSound.IsChecked;
            Settings.Music = MenuMusic.IsChecked;
            Settings.Speech = MenuSpeech.IsChecked;
            controller.ApplySettings();
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
            {
                return;
            }
            Settings.SoundVolume = SoundVolume.Value;
            Settings.MusicVolume = MusicVolume.Value;
            controller.ApplySettings();
        }

        private void OnScrollingBackground(object sender, RoutedEventArgs e)
        {
            Settings.ScrollingBackground = MenuScrolling.IsChecked;
            controller.ApplySettings();
            SyncMenus();
        }

        private void OnSmoothMotion(object sender, RoutedEventArgs e)
        {
            Settings.SmoothMotion = MenuSmooth.IsChecked;
            controller.ApplySettings();
            shownFrame = -1; // Back to the game's own frames at once when switched off
        }

        private void OnSmartAim(object sender, RoutedEventArgs e)
        {
            Settings.SmartAim = MenuSmartAim.IsChecked;
            controller.ApplySettings();
        }

        private void OnUpdateSpeed(object sender, RoutedEventArgs e)
        {
            int? speed = Dialogs.AskUpdateSpeed(this, Settings.UpdateSpeed);
            if (speed != null)
            {
                Settings.UpdateSpeed = speed.Value;
            }
        }

        private void OnResolution(object sender, RoutedEventArgs e)
        {
            int res = int.Parse((string)((MenuItem)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
            controller.SetResolution((ArtResolution)res);
            SyncMenus();
            LayoutStage();
        }

        private void OnPlayfieldWidth(object sender, RoutedEventArgs e)
        {
            int cells = int.Parse((string)((MenuItem)sender).Tag, System.Globalization.CultureInfo.InvariantCulture);
            controller.SetPlayfieldWidth(cells);
            SyncMenus();
            LayoutStage();
        }

        private void OnRemapKeys(object sender, RoutedEventArgs e) => ShowControls(remap: true);

        private void OnHallOfFame(object sender, RoutedEventArgs e) => ShowHallOfFame(Game.World is >= 1 and <= 10 ? Game.World : 1, Game.Level);

        private void OnHelp(object sender, RoutedEventArgs e) => ShowControls(remap: false);

        // Help > Controls and Options > Remap Keys. Saved keys go to the profile playing (their .thg) and become the
        // layout the title screen and new profiles start with.
        private void ShowControls(bool remap)
        {
            Game.Input.ReleaseAll();
            var window = new ControlsWindow(Game.Art, Game.Palette, Game.KeyBindings, controller.KeyName, remap) { Owner = this };
            Dialogs.ShowModal(window);
            if (window.Result is { } keys)
            {
                keys.CopyTo(Game.KeyBindings, 0);
                if (Game.SessionActive)
                {
                    Game.SaveThg();
                }
                Settings.KeyBindings = keys;
                SaveSettingsNow();
            }
        }

        private void OnAbout(object sender, RoutedEventArgs e)
        {
            Dialogs.ShowModal(new AboutWindow(Game.Art, Game.Palette, Settings.MenuAnimations, on =>
            {
                Settings.MenuAnimations = on;
                SaveSettingsNow();
                TitleMenu.Animated = on;
            }) { Owner = this });
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            UpdatePanels();
            bool minimized = WindowState == WindowState.Minimized;
            if (minimized == controller.Minimized)
            {
                return;
            }
            controller.Minimized = minimized;
            if (minimized)
            {
                controller.Music.Stop();
                Game.Sounds.StopAll();
            }
            else
            {
                controller.Music.Resume();
            }
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!closingConfirmed)
            {
                if (!Game.CanExit)
                {
                    Dialogs.Message(this, "Cannot exit the game between levels.", "Thexder");
                    e.Cancel = true;
                    return;
                }
                if (!Dialogs.AskYesNo(this, "Are you sure you wish to leave Thexder?", "Thexder"))
                {
                    e.Cancel = true;
                    return;
                }
            }
            CompositionTarget.Rendering -= OnRendering;
            if (Application.Current != null)
            {
                Application.Current.Activated -= OnApplicationActivated;
                Application.Current.Deactivated -= OnApplicationDeactivated;
            }
            Game.SaveOnExit();
            Settings.Panels = Enum.GetValues<PanelSlot>().ToDictionary(slot => slot.ToString(), dock.Placement);
            Settings.PanelScale = dock.PanelScale;
            Settings.MapHeight = dock.MapHeight;
            foreach (var panel in new[] { weaponsWindow, equipmentWindow, enemyWindow, mapWindow })
            {
                if (panel != null)
                {
                    panel.AllowClose = true;
                    panel.Close();
                }
            }
            Settings.Window = WindowState == WindowState.Minimized || WindowStyle == WindowStyle.None
                ? Settings.Window
                : new WindowPlacement(RestoreBounds.Left, RestoreBounds.Top, RestoreBounds.Width, RestoreBounds.Height, WindowState == WindowState.Maximized);
            settingsService.Save();
            controller.Dispose();
            audio?.Dispose();
        }

        private bool RestorePlacement(WindowPlacement? placement)
        {
            if (placement == null || placement.Width < 200 || placement.Height < 200)
            {
                return false;
            }
            var area = SystemParameters.VirtualScreenWidth;
            if (placement.Left < SystemParameters.VirtualScreenLeft - 50 || placement.Left > SystemParameters.VirtualScreenLeft + area - 100)
            {
                return false;
            }
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
            Width = placement.Width;
            Height = placement.Height;
            if (placement.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
            return true;
        }

        void IGameView.ShowTitle(IReadOnlyList<string> players, string lastPlayer)
        {
            var profiles = players.Select(p => (p, Game.ProfileProgress(p) is { } g
                ? $"Classic · World {g.World} · Level {g.Level} · {Game.DifficultyName(g.Difficulty)}"
                : "No classic game yet")).ToList();
            ScoreOverlay.Visibility = Visibility.Collapsed;
            StageHost.Visibility = Visibility.Hidden;
            TitleMenu.Open(Game.Art, Game.Palette, profiles, lastPlayer);
            SyncMenus();
        }

        // The main menu's bin: after a warning, the profile and everything it saved go.
        private void DeleteProfile(string name)
        {
            if (Dialogs.Message(this, $"Delete the profile \"{name}\"?\n\nIts saved games, progress, scores and Hall of Fame entries are removed. " +
                "This cannot be undone.", "Delete Profile", DialogIcon.Warning, yesNo: true, okText: "Delete", noText: "Cancel") != true)
            {
                return;
            }
            Game.DeleteProfile(name);
            if (Settings.PlayerName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                Settings.PlayerName = "";
                SaveSettingsNow();
            }
            ((IGameView)this).ShowTitle(Game.KnownPlayers().ToList(), Settings.PlayerName);
        }

        void IGameView.HideTitle()
        {
            TitleMenu.Close();
            StageHost.Visibility = Visibility.Visible;
            shownFrame = -1; // Present the stage again at once
            FocusGame();
        }

        // The keys go to the window itself.
        private void FocusGame()
        {
            FocusManager.SetFocusedElement(this, null);
            if (IsActive)
            {
                Keyboard.Focus(this);
            }
        }

        void IGameView.ShowScoreButtons(bool show) => ScoreOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        void IGameView.MovieStarted()
        {
            MovieImage.Source = null;
            MovieOverlay.Visibility = Visibility.Visible;
        }

        void IGameView.MovieStopped() => MovieOverlay.Visibility = Visibility.Collapsed;

        void IGameView.ShowDemoCard(byte[] bgra, int width, int height)
        {
            moviePresenter.PresentBgra(bgra, width, height);
            MovieImage.Source = moviePresenter.Bitmap;
            MovieOverlay.Visibility = Visibility.Visible;
        }

        void IGameView.HideDemoCard() => MovieOverlay.Visibility = Visibility.Collapsed;

        string? IGameView.AskCodeword() => Dialogs.AskText(this, "Encoded Features", "Enter Code Word:", 30);

        public bool AskYesNo(string text, string caption)
        {
            Game.Input.ReleaseAll();
            return Dialogs.AskYesNo(this, text, caption);
        }

        public void ShowMessage(string text, string caption)
        {
            Game.Input.ReleaseAll();
            Dialogs.Message(this, text, caption);
        }

        int? IGameView.ChooseGame(IReadOnlyList<(string Title, string Detail)> rows, int preselect)
        {
            Game.Input.ReleaseAll();
            var choice = Dialogs.Choose(this, "Game Select", "Select the game you wish to play:", rows.Select(r => new Choice(r.Title, r.Detail)).ToList(), preselect);
            SyncMenus();
            return choice;
        }

        public int ChooseDifficulty(IReadOnlyList<string> names, int current)
        {
            Game.Input.ReleaseAll();
            return Dialogs.ChooseDifficulty(this, names, current) ?? current;
        }

        public void ShowHallOfFame(int world, int level)
        {
            Game.Input.ReleaseAll();
            Dialogs.HallOfFame(this, Game, world, level);
        }

        void IGameView.Quit()
        {
            closingConfirmed = true;
            Close();
        }
    }
}
