using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Thexder.Feature.Procedural;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using Image = System.Windows.Controls.Image;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Thexder.UI
{
    /// <summary>The ways to play, as the Game Mode dialog offers them.</summary>
    internal enum GameModeOption
    {
        Classic,
        Campaign,
        RandomMap,
    }

    /// <summary>A generated game to start: its settings, and whether to continue the saved one.</summary>
    internal sealed record ProcSetup(ProcSave Settings, bool Resume);

    /// <summary>A preview of a generated map: pixels (BGRA, one per cell) and a line about it.</summary>
    internal sealed record MapPicture(uint[] Pixels, int Width, int Height, string Caption);

    // The game mode and generated-game dialogs, in the dialogs' compact style.
    internal static partial class Dialogs
    {
        private static readonly string[] LengthNames = { "3 worlds", "5 worlds", "10 worlds (like the classic campaign)", "Endless" };
        private static readonly int[] LengthValues = { 3, 5, 10, 0 };

        /// <summary>After logging in: the classic campaign, a generated campaign or a random map.</summary>
        public static GameModeOption? ChooseGameMode(Window? owner, string player, string classicDetail, string campaignDetail, string mapDetail, GameModeOption preselect)
        {
            var window = NewWindow(owner, "Game Mode", 440);
            var list = new ListBox();
            list.SetResourceReference(FrameworkElement.StyleProperty, "CompactList");
            list.ItemContainerStyle = Application.Current?.TryFindResource("CompactListItem") as Style;
            void Add(SymbolRegular symbol, string title, string text, string detail)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var icon = new SymbolIcon { Symbol = symbol, FontSize = 22, Margin = new Thickness(2, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                icon.SetResourceReference(Control.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
                row.Children.Add(icon);
                var lines = new StackPanel();
                var head = new DockPanel();
                var name = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold };
                head.Children.Add(name);
                if (detail.Length > 0)
                {
                    var right = Dim(detail);
                    right.HorizontalAlignment = HorizontalAlignment.Right;
                    right.Margin = new Thickness(12, 0, 0, 0);
                    DockPanel.SetDock(right, Dock.Right);
                    head.Children.Insert(0, right);
                }
                lines.Children.Add(head);
                var description = Dim(text);
                description.TextWrapping = TextWrapping.Wrap;
                description.TextTrimming = TextTrimming.None;
                lines.Children.Add(description);
                Grid.SetColumn(lines, 1);
                row.Children.Add(lines);
                list.Items.Add(row);
            }
            Add(SymbolRegular.History24, "Classic Campaign", "The classic ten planets and fifty levels.", classicDetail);
            Add(SymbolRegular.Globe24, "Procedural Campaign", "A new tour of the planets every time: five generated levels per world, each world ending with its boss.", campaignDetail);
            Add(SymbolRegular.Map24, "Random Map", "One generated level, any size, on the planet you choose.", mapDetail);
            list.SelectedIndex = (int)preselect;

            GameModeOption? result = null;
            var ok = PrimaryButton("Continue");
            ok.Click += (_, _) =>
            {
                if (list.SelectedIndex >= 0)
                {
                    result = (GameModeOption)list.SelectedIndex;
                    window.DialogResult = true;
                }
            };
            list.MouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListBoxItem)
                {
                    ok.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                }
            };
            var body = new StackPanel();
            body.Children.Add(Text($"How do you want to play, {player}?"));
            body.Children.Add(list);
            var hint = Dim("Network play: File › Connect, during any game.");
            hint.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(hint);
            SetContent(window, body, ok, SecondaryButton("Back", cancel: true));
            FocusList(window, list);
            Run(window);
            return result;
        }

        private static void FocusList(Window window, ListBox list)
        {
            window.Loaded += (_, _) =>
            {
                list.Focus();
                if (list.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, list.SelectedIndex)) is ListBoxItem item)
                {
                    item.Focus();
                }
            };
        }

        // A grid of "label - control" rows inside a frame.
        private static (Border Frame, Action<string, UIElement> Row) Form(double controlWidth = 190)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(controlWidth) });
            void Row(string label, UIElement control)
            {
                int r = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var text = new TextBlock { FontSize = 12, Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
                Grid.SetRow(text, r);
                grid.Children.Add(text);
                if (control is FrameworkElement fe)
                {
                    fe.Margin = new Thickness(0, 2, 0, 2);
                }
                Grid.SetRow(control, r);
                Grid.SetColumn(control, 1);
                grid.Children.Add(control);
            }
            var frame = new Border { Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(6), Child = grid };
            frame.SetResourceReference(FrameworkElement.StyleProperty, "Frame");
            return (frame, Row);
        }

        private static ComboBox Combo(IEnumerable<string> items, int selected)
        {
            var combo = new ComboBox { FontSize = 12 };
            foreach (string item in items)
            {
                combo.Items.Add(item);
            }
            combo.SelectedIndex = Math.Clamp(selected, 0, combo.Items.Count - 1);
            return combo;
        }

        // A seed box with a button that rolls a new one.
        private static (FrameworkElement Panel, Func<ulong> Value, Action<ulong> Set, NumberBox Box) SeedBox(ulong seed)
        {
            var box = new NumberBox
            {
                Minimum = 1,
                Maximum = 999_999_999,
                Value = seed,
                MaxDecimalPlaces = 0,
                ClearButtonEnabled = false,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            };
            var roll = new Button { Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowShuffle24 }, Padding = new Thickness(6), Margin = new Thickness(6, 0, 0, 0), ToolTip = "A new random seed" };
            roll.Click += (_, _) => box.Value = ProcGame.NewSeed();
            var panel = new DockPanel();
            DockPanel.SetDock(roll, Dock.Right);
            panel.Children.Add(roll);
            panel.Children.Add(box);
            return (panel, () => (ulong)Math.Clamp(box.Value ?? 1, 1, 999_999_999), v => box.Value = v, box);
        }

        /// <summary>The Procedural Campaign: continue the saved one, or set up a new one.</summary>
        public static ProcSetup? CampaignSetup(Window? owner, ProcSave? saved, string savedSummary, ProcSave defaults, IReadOnlyList<string> difficultyNames)
        {
            var window = NewWindow(owner, "Procedural Campaign", 420);
            var body = new StackPanel();
            ProcSetup? result = null;
            Button? resume = null;
            if (saved != null)
            {
                body.Children.Add(Heading("Your campaign"));
                var line = Dim(savedSummary);
                line.TextWrapping = TextWrapping.Wrap;
                line.TextTrimming = TextTrimming.None;
                line.Margin = new Thickness(0, 0, 0, 12);
                body.Children.Add(line);
                resume = PrimaryButton("Continue");
                resume.Click += (_, _) =>
                {
                    result = new ProcSetup(saved, Resume: true);
                    window.DialogResult = true;
                };
            }
            body.Children.Add(Heading(saved != null ? "Or a new campaign" : "A new campaign"));
            var size = Combo(MapSizes.All.Select(MapSizes.Name), (int)defaults.Size);
            var sizeNote = Dim(MapSizes.Describe(defaults.Size));
            size.SelectionChanged += (_, _) => sizeNote.Text = MapSizes.Describe(MapSizes.All[Math.Max(0, size.SelectedIndex)]);
            var length = Combo(LengthNames, Array.IndexOf(LengthValues, defaults.Length) is int li && li >= 0 ? li : 2);
            var difficulty = Combo(difficultyNames, defaults.Difficulty);
            var seed = SeedBox(defaults.Seed);
            var (frame, row) = Form();
            row("Map size", size);
            row("", sizeNote);
            row("Length", length);
            row("Difficulty", difficulty);
            row("Seed", seed.Panel);
            body.Children.Add(frame);
            var note = Dim("The same seed and settings always build the same planets.");
            note.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(note);

            var start = saved != null ? SecondaryButton("Start new") : PrimaryButton("Start");
            start.Click += (_, _) =>
            {
                if (saved != null && !AskYesNo(window, "Start a new campaign? Your current one will be lost.", "Procedural Campaign"))
                {
                    return;
                }
                result = new ProcSetup(new ProcSave
                {
                    Kind = ProcKind.Campaign,
                    Seed = seed.Value(),
                    Size = MapSizes.All[Math.Max(0, size.SelectedIndex)],
                    Length = LengthValues[Math.Max(0, length.SelectedIndex)],
                    Difficulty = Math.Max(0, difficulty.SelectedIndex),
                }, Resume: false);
                window.DialogResult = true;
            };
            var back = SecondaryButton("Back", cancel: true);
            if (resume != null)
            {
                SetContent(window, body, resume, start, back);
            }
            else
            {
                SetContent(window, body, start, back);
            }
            Run(window);
            return result;
        }

        /// <summary>
        /// The Random Map: planet, size, difficulty, boss and seed, with a picture of the map the settings build.
        /// </summary>
        public static ProcSave? RandomMapSetup(Window? owner, ProcSave defaults, IReadOnlyList<string> difficultyNames, IReadOnlyList<string> planetNames, Func<ProcSave, Task<MapPicture?>> preview)
        {
            var window = NewWindow(owner, "Random Map", 500);
            var planet = Combo(new[] { "Any planet" }.Concat(planetNames.Select((n, i) => $"{i + 1} · {n}")), defaults.Theme);
            var size = Combo(MapSizes.All.Select(s => $"{MapSizes.Name(s)} · {MapSizes.Of(s).Width} × {MapSizes.Of(s).Height}"), (int)defaults.Size);
            var difficulty = Combo(difficultyNames, defaults.Difficulty);
            var boss = new CheckBox { Content = "End with the planet's boss", IsChecked = defaults.Boss, FontSize = 12 };
            var seed = SeedBox(defaults.Seed);
            var (frame, row) = Form(230);
            row("Planet", planet);
            row("Map size", size);
            row("Difficulty", difficulty);
            row("Seed", seed.Panel);
            row("", boss);

            var picture = new Image { Stretch = Stretch.Uniform, Height = 118, Margin = new Thickness(0, 10, 0, 4) };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            var pictureFrame = new Border { CornerRadius = new CornerRadius(4), Child = picture, ClipToBounds = true };
            var caption = Dim("Building the map…");
            var body = new StackPanel();
            body.Children.Add(frame);
            body.Children.Add(pictureFrame);
            body.Children.Add(caption);

            ProcSave Current()
            {
                int theme = Math.Max(0, planet.SelectedIndex);
                ulong s = seed.Value();
                return new ProcSave
                {
                    Kind = ProcKind.Single,
                    Seed = s,
                    Theme = theme == 0 ? (int)(s % 10) + 1 : theme,
                    Size = MapSizes.All[Math.Max(0, size.SelectedIndex)],
                    Difficulty = Math.Max(0, difficulty.SelectedIndex),
                    Boss = boss.IsChecked == true,
                };
            }

            int generation = 0;
            var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            debounce.Tick += async (_, _) =>
            {
                debounce.Stop();
                int mine = ++generation;
                caption.Text = "Building the map…";
                Serilog.Log.Debug("Random map preview {Generation}", mine);
                var shot = await preview(Current());
                Serilog.Log.Debug("Random map preview {Generation} done: {Ok}", mine, shot != null);
                if (mine != generation || shot == null)
                {
                    return;
                }
                var bitmap = BitmapSource.Create(shot.Width, shot.Height, 96, 96, PixelFormats.Bgra32, null, shot.Pixels, shot.Width * 4);
                bitmap.Freeze();
                picture.Source = bitmap;
                caption.Text = shot.Caption;
            };
            void Changed()
            {
                debounce.Stop();
                debounce.Start();
            }
            planet.SelectionChanged += (_, _) => Changed();
            size.SelectionChanged += (_, _) => Changed();
            difficulty.SelectionChanged += (_, _) => Changed();
            boss.Checked += (_, _) => Changed();
            boss.Unchecked += (_, _) => Changed();
            seed.Box.ValueChanged += (_, _) => Changed();

            ProcSave? result = null;
            var play = PrimaryButton("Play");
            play.Click += (_, _) =>
            {
                result = Current();
                window.DialogResult = true;
            };
            SetContent(window, body, play, SecondaryButton("Back", cancel: true));
            window.Loaded += (_, _) => Changed();
            window.Closed += (_, _) => debounce.Stop();
            Run(window);
            return result;
        }

        /// <summary>
        /// A random map is done: true for another one with the same settings, false for the Game Mode dialog.
        /// </summary>
        public static bool MapComplete(Window? owner, string text)
        {
            var window = NewWindow(owner, "Map Complete", 360);
            bool another = false;
            var next = PrimaryButton("Another map");
            next.Click += (_, _) =>
            {
                another = true;
                window.DialogResult = true;
            };
            var menu = SecondaryButton("Game mode", cancel: true);
            SetContent(window, Text(text, 0), next, menu);
            window.Loaded += (_, _) => next.Focus();
            Run(window);
            return another;
        }
    }
}
