using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Thexder.Core.Game;
using Thexder.Feature;

namespace Thexder.UI
{
    // Developer tool: drives the real window from a script without touching the desktop's input, and renders every game
    // window (main and side windows) at its screen position into PNGs.
    public partial class MainWindow
    {
        private Queue<string>? uiScript;
        private string uiScriptDir = "";
        private int uiSnapshot;

        private void StartUiScript(string dir, string script)
        {
            uiScriptDir = dir;
            Directory.CreateDirectory(dir);
            uiScript = new Queue<string>(script.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (script.Contains('F'))
            {
                Application.Current.Activated += (_, _) => LogFocusLine("event: application activated");
                Application.Current.Deactivated += (_, _) => LogFocusLine("event: application deactivated");
                Activated += (_, _) => LogFocusLine("event: main window activated");
                Deactivated += (_, _) => LogFocusLine("event: main window deactivated");
                Keyboard.AddGotKeyboardFocusHandler(this, (_, e) => LogFocusLine($"event: keyboard focus -> {DescribeElement(e.NewFocus)}"));
                Keyboard.AddLostKeyboardFocusHandler(this, (_, e) => LogFocusLine($"event: keyboard focus lost to {DescribeElement(e.NewFocus)}"));
                AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, e) => LogFocusLine($"event: key down {e.Key} reached the main window")), true);
            }
            Dispatcher.BeginInvoke(NextUiStep, DispatcherPriority.ApplicationIdle);
        }

        private void NextUiStep()
        {
            if (uiScript == null || uiScript.Count == 0)
            {
                return;
            }
            string token = uiScript.Dequeue();
            char op = token[0];
            string arg = token[1..];
            int delay = op == 'w' ? int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) : 50;
            // The next step is scheduled first: a token can open a modal dialog, whose own message loop runs the rest.
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                NextUiStep();
            };
            timer.Start();
            switch (op)
            {
                case 'A':
                    Activate();
                    break;
                case 'K':
                case 'U':
                    PostKey(int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), op == 'K');
                    break;
                case 'F':
                    LogFocus(arg);
                    break;
                case 'a':
                    if (TitleMenu.IsOpen)
                    {
                        TitleMenu.Play();
                    }
                    else
                    {
                        Game.Advance();
                    }
                    break;
                case 'l':
                    Login(arg);
                    break;
                case 'n':
                    OnScoreContinue(this, new RoutedEventArgs());
                    break;
                case 'r':
                    OnScoreReplay(this, new RoutedEventArgs());
                    break;
                case '+':
                    Game.Input.SetAction(Enum.Parse<GameAction>(arg, ignoreCase: true), true);
                    break;
                case '-':
                    Game.Input.SetAction(Enum.Parse<GameAction>(arg, ignoreCase: true), false);
                    break;
                case 'c':
                    Game.ApplyCodeword(arg);
                    break;
                case 'd':
                    OnStandardConfiguration(this, new RoutedEventArgs());
                    break;
                case 'g':
                case 'z':
                {
                    string[] p = arg.Split(',');
                    var panel = PanelOf(Enum.Parse<PanelSlot>(p[0], ignoreCase: true));
                    if (panel != null)
                    {
                        SimulateMoveSize(panel, op == 'z', double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
                            p.Length > 2 ? double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) : 0);
                    }
                    break;
                }
                case 'L':
                    if (arg.Equals("fit", StringComparison.OrdinalIgnoreCase))
                    {
                        FitToWorkArea();
                    }
                    else
                    {
                        var (w, h) = ParsePreset(arg);
                        ApplyLayout(MonitorArea.Of(this).WorkFor(w, h));
                    }
                    break;
                case 'M':
                    timer.Stop(); // the dropdown needs a moment to open and lay out
                    SaveMenuPicture(arg, Path.Combine(uiScriptDir, $"{uiSnapshot++:000}-menu-{arg}.png"), timer.Start);
                    break;
                case 'y':
                    LogWindowRects();
                    break;
                case 'P':
                    timer.Stop(); // A preview can wait inside; the next step comes after it
                    PreviewDialog(arg);
                    timer.Start();
                    break;
                case 'H':
                    controller.Net.Host("Test game", new NetGameParameters(int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), 10, 0, true, true, true),
                        Services.Network.NetSession.DefaultPort, loopbackOnly: true);
                    ShowNetComm();
                    SyncMenus();
                    break;
                case 'J':
                    _ = controller.Net.JoinAsync(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, Services.Network.NetSession.DefaultPort));
                    break;
                case 'X':
                    controller.Net.Leave();
                    SyncMenus();
                    break;
                case 'T':
                    smoothTraceUntil = clock.ElapsedMilliseconds + int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case 's':
                    SaveWindowsSnapshot(Path.Combine(uiScriptDir, $"{uiSnapshot++:000}-{(arg.Length == 0 ? "ui" : arg)}.png"));
                    break;
                case 'q':
                    timer.Stop();
                    closingConfirmed = true;
                    Close();
                    return;
            }
        }

        private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101;

        private void PostKey(int vk, bool down)
        {
            IntPtr target = Utils.NativeMethods.GetFocus();
            if (target == IntPtr.Zero)
            {
                target = Utils.NativeMethods.GetActiveWindow();
            }
            uint scan = Utils.NativeMethods.MapVirtualKey((uint)vk, 0);
            bool extended = vk is >= 0x21 and <= 0x2e; // Arrows, Insert, Delete, Home, End, Page Up/Down
            long lParam = 1 | (scan << 16) | (extended ? 1L << 24 : 0) | (down ? 0 : (1L << 30) | (1L << 31));
            Utils.NativeMethods.PostMessage(target, down ? WmKeyDown : WmKeyUp, vk, (IntPtr)lParam);
            LogFocusLine($"posted vk {vk} {(down ? "down" : "up")} to {DescribeWindow(target)}");
        }

        private void LogFocus(string label)
        {
            var focused = Keyboard.FocusedElement;
            var logical = FocusManager.GetFocusedElement(this);
            LogFocusLine($"[{label}] active={IsActive} foreground={DescribeWindow(Utils.NativeMethods.GetForegroundWindow())} "
                + $"activeWindow={DescribeWindow(Utils.NativeMethods.GetActiveWindow())} focusHwnd={DescribeWindow(Utils.NativeMethods.GetFocus())} "
                + $"keyboardFocus={DescribeElement(focused)} logicalFocus={DescribeElement(logical)} inputActive={Game.Input.Active} "
                + $"right={Game.Input.Held(GameAction.Right)} player={Game.Player.X},{Game.Player.Y} state={Game.Player.PlayState} movie={Game.MoviePlaying} "
                + $"overlay={MovieOverlay.Visibility} source={(MovieImage.Source == null ? "null" : MovieImage.Source == moviePresenter.Bitmap ? "bitmap" : "other")} dialogs={Dialogs.OpenCount} "
                + $"generated={Game.LevelGenerated} map={Game.MapW}x{Game.MapH} slots={Game.Enemies.Count(t => t.State == EnemyRecord.Alive)} reserve={Game.EnemyReserve.Count} label='{Game.LevelLabel}' world={Game.World} level={Game.Level} energy={Game.Player.Energy}");
        }

        private void LogFocusLine(string text) =>
            File.AppendAllText(Path.Combine(uiScriptDir, "focus.log"), $"{clock.ElapsedMilliseconds} {text}{Environment.NewLine}");

        private string DescribeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return "none";
            }
            foreach (Window w in Application.Current.Windows)
            {
                if (new System.Windows.Interop.WindowInteropHelper(w).Handle == hwnd)
                {
                    return w == this ? "main" : $"'{w.Title}'";
                }
            }
            return $"0x{hwnd:x}";
        }

        private static string DescribeElement(IInputElement? element) => element switch
        {
            null => "none",
            Window w => $"window '{w.Title}'",
            FrameworkElement f when f.Name.Length > 0 => $"{f.GetType().Name} {f.Name}",
            FrameworkElement f => $"{f.GetType().Name} in {(Window.GetWindow(f)?.Title ?? "?")}",
            _ => element.GetType().Name,
        };

        // Opens one of the dialogs through the Dialogs.Preview hook and saves a picture of it.
        private void PreviewDialog(string name)
        {
            string path = Path.Combine(uiScriptDir, $"{uiSnapshot++:000}-dialog-{name}.png");
            Dialogs.Preview = window =>
            {
                window.Show();
                window.UpdateLayout();
                if (name is "randommap" or "about")
                {
                    Serilog.Log.Information("Preview: waiting for the picture to build");
                    // Let the map preview build and appear
                    var frame = new DispatcherFrame();
                    var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
                    wait.Tick += (_, _) =>
                    {
                        wait.Stop();
                        frame.Continue = false;
                    };
                    wait.Start();
                    Dispatcher.PushFrame(frame);
                    window.UpdateLayout();
                }
                SaveWindowPicture(window, path);
                window.Close();
            };
            try
            {
                switch (name)
                {
                    case "difficulty":
                        Dialogs.ChooseDifficulty(this, Enumerable.Range(0, 5).Select(Game.DifficultyName).ToList(), 2);
                        break;
                    case "games":
                        ((IGameView)this).ChooseGame(Enumerable.Range(0, 5).Select(d => (Game.DifficultyName(d), $"World {d + 1} · Level {5 - d}")).ToList(), 2);
                        break;
                    case "revert":
                        Dialogs.Choose(this, "Revert", "Select a previously played world:",
                            new[] { new Choice($"World 1 · {Game.PlanetName(1)}", Game.DifficultyName(2)), new Choice($"World 2 · {Game.PlanetName(2)}", Game.DifficultyName(2)) }, 0);
                        break;
                    case "yesno":
                        Dialogs.AskYesNo(this, "Are you sure you wish to leave Thexder?", "Thexder");
                        break;
                    case "message":
                        Dialogs.Message(this, "Thexder game paused.\nClick OK to continue.", "Thexder");
                        break;
                    case "keys":
                        OnRemapKeys(this, new RoutedEventArgs());
                        break;
                    case "speed":
                        Dialogs.AskUpdateSpeed(this, Settings.UpdateSpeed);
                        break;
                    case "codeword":
                        Dialogs.AskText(this, "Encoded Features", "Enter Code Word:", 30);
                        break;
                    case "hof":
                        Dialogs.HallOfFame(this, Game, 1, 1);
                        break;
                    case "help":
                        OnHelp(this, new RoutedEventArgs());
                        break;
                    case "about":
                        OnAbout(this, new RoutedEventArgs());
                        break;
                    case "mode":
                        Dialogs.ChooseGameMode(this, "Tester", "World 3 · Level 2", "World 2 · Level 4", "", GameModeOption.Campaign);
                        break;
                    case "campaign":
                        Dialogs.CampaignSetup(this, new Feature.Procedural.ProcSave { Stage = 2, Level = 4, Seed = 123456 }, "Planet, level 4, world 2 of 10 · Standard maps · Normal · seed 123456 · score 12000",
                            new Feature.Procedural.ProcSave { Seed = 987654 }, Enumerable.Range(0, 5).Select(Game.DifficultyName).ToList());
                        break;
                    case "randommap":
                        Dialogs.RandomMapSetup(this, new Feature.Procedural.ProcSave { Seed = 4242, Theme = 5, Size = Feature.Procedural.MapSize.Large }, Enumerable.Range(0, 5).Select(Game.DifficultyName).ToList(),
                            Enumerable.Range(1, 10).Select(Game.PlanetName).ToList(), s => PreviewMap(controller.Library, s));
                        break;
                }
            }
            finally
            {
                Dialogs.Preview = null;
            }
        }

        // One window as it looks on screen (Mica shows as the dark theme background).
        // Saves a picture of a top-level menu's dropdown (by its header, without the access-key underscore). A menu's own
        // dropdown does not open in a window without the focus, so a context menu with the same rows stands in for it:
        // its rows use the same templates. The row heights go to focus.log.
        private void SaveMenuPicture(string header, string path, Action done)
        {
            var menu = MainMenu.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(m => (m.Header as string)?.Replace("_", "") == header);
            if (menu == null)
            {
                done();
                return;
            }
            var copy = new System.Windows.Controls.ContextMenu { PlacementTarget = MainMenu, FontSize = MainMenu.FontSize };
            foreach (var entry in menu.Items)
            {
                if (entry is System.Windows.Controls.MenuItem m)
                {
                    var row = new System.Windows.Controls.MenuItem
                    {
                        Header = m.Header,
                        IsCheckable = m.IsCheckable,
                        IsChecked = m.IsChecked,
                        IsEnabled = m.IsEnabled,
                        InputGestureText = m.InputGestureText,
                        FontSize = m.FontSize,
                        FontWeight = m.FontWeight,
                    };
                    if (m.HasItems)
                    {
                        row.Items.Add(new System.Windows.Controls.MenuItem { Header = "..." });
                    }
                    copy.Items.Add(row);
                }
                else
                {
                    copy.Items.Add(new System.Windows.Controls.Separator());
                }
            }
            copy.IsOpen = true;
            Dispatcher.BeginInvoke(() =>
            {
                var heights = copy.Items.OfType<FrameworkElement>().Select(r => r.ActualHeight.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                LogFocusLine($"menu {header}: {copy.ActualWidth:0} x {copy.ActualHeight:0}, rows {string.Join(" ", heights)}");
                if (copy.ActualWidth >= 1 && copy.ActualHeight >= 1)
                {
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(copy.ActualWidth), (int)Math.Ceiling(copy.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(copy);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(path);
                    encoder.Save(stream);
                }
                copy.IsOpen = false;
                done();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found || (found = FindChild<T>(child)!) != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void SaveWindowPicture(Window window, string path)
        {
            if (window.Content is not FrameworkElement root)
            {
                return;
            }
            double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            int w = (int)Math.Ceiling(root.ActualWidth * scale), h = (int)Math.Ceiling(root.ActualHeight * scale);
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null, new System.Windows.Rect(0, 0, root.ActualWidth, root.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(background);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private long smoothTraceUntil;

        // T<ms>: one line per display frame (time, fraction, camera pixels, whether the smooth frame was shown).
        private void TraceSmooth(double start, double renderingTime)
        {
            if (uiScriptDir.Length == 0 || clock.ElapsedMilliseconds > smoothTraceUntil)
            {
                return;
            }
            var (x, y, a) = Game.LastSmoothCamera;
            var p = Game.Player;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            File.AppendAllText(Path.Combine(uiScriptDir, "smooth.log"), string.Create(inv,
                $"{start:0} a={a:0.00} cam={x},{y} player={p.X},{p.Y} frame={Game.FrameCounter} smooth={smoothShown} work={clock.Elapsed.TotalMilliseconds - start:0.0} vsync={renderingTime:0.0}{Environment.NewLine}"));
        }

        private PanelWindow? PanelOf(PanelSlot slot) => slot switch
        {
            PanelSlot.Weapons => weaponsWindow,
            PanelSlot.Equipment => equipmentWindow,
            PanelSlot.Enemy => enemyWindow,
            _ => mapWindow,
        };

        // Drives a panel through the same window messages a mouse drag or resize produces, in ten steps.
        private static void SimulateMoveSize(PanelWindow panel, bool resize, double dx, double dy)
        {
            const uint WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232, WM_SIZING = 0x0214, WM_MOVING = 0x0216;
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(panel).Handle;
            Utils.NativeMethods.SendMessage(hwnd, WM_ENTERSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
            for (int i = 0; i < 10; i++)
            {
                Utils.NativeMethods.SendMessage(hwnd, resize ? WM_SIZING : WM_MOVING, IntPtr.Zero, IntPtr.Zero);
                if (resize)
                {
                    panel.Width += dx / 10;
                }
                else
                {
                    panel.Left += dx / 10;
                    panel.Top += dy / 10;
                }
            }
            Utils.NativeMethods.SendMessage(hwnd, WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
        }

        private void LogWindowRects()
        {
            string Rect(Window w) => $"{w.Left:0},{w.Top:0} {w.ActualWidth:0}x{w.ActualHeight:0}";
            var lines = new List<string> { $"main {Rect(this)}" };
            foreach (var slot in Enum.GetValues<PanelSlot>())
            {
                if (PanelOf(slot) is { } p)
                {
                    lines.Add($"{slot} {(dock.IsDocked(slot) ? "docked" : "floating")} {(p.IsVisible ? "" : "hidden ")}{Rect(p)}");
                }
            }
            lines.Add($"panel scale {dock.PanelScale:0.00}");
            File.AppendAllLines(Path.Combine(uiScriptDir, "windows.log"), lines.Append(""));
        }

        /// <summary>Renders the main window and the visible side windows where they are on screen.</summary>
        private void SaveWindowsSnapshot(string path)
        {
            var windows = new List<Window> { this };
            windows.AddRange(OwnedWindows.Cast<Window>().Where(w => w.IsVisible));
            double left = windows.Min(w => w.Left), top = windows.Min(w => w.Top);
            double right = windows.Max(w => w.Left + w.ActualWidth), bottom = windows.Max(w => w.Top + w.ActualHeight);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), null, new System.Windows.Rect(0, 0, right - left, bottom - top));
                foreach (var w in windows)
                {
                    if (w.Content is not FrameworkElement root || root.ActualWidth < 1)
                    {
                        continue;
                    }
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var origin = new System.Windows.Rect(w.Left - left, w.Top - top, root.ActualWidth, root.ActualHeight);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null, origin);
                    dc.DrawImage(bitmap, origin);
                    dc.DrawRectangle(null, new Pen(Brushes.DimGray, 1), origin);
                }
            }
            var all = new RenderTargetBitmap((int)Math.Ceiling(right - left), (int)Math.Ceiling(bottom - top), 96, 96, PixelFormats.Pbgra32);
            all.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(all));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
    }
}
