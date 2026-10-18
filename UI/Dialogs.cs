using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Thexder.Core.Game;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;

namespace Thexder.UI
{
    /// <summary>
    /// One entry of a choice list: a title, and on the right an optional dimmed detail or a rating of <paramref
    /// name="Rating"/> filled dots out of <paramref name="RatingMax"/>.
    /// </summary>
    internal readonly record struct Choice(string Title, string? Detail = null, int Rating = 0, int RatingMax = 0);

    internal static class KeyNames
    {
        /// <summary>For the key lists: "Add key" becomes "Num +", "Up arrow key" becomes "Up arrow".</summary>
        public static string Friendly(string original)
        {
            string name = original.EndsWith(" key", StringComparison.Ordinal) ? original[..^4] : original;
            return name switch
            {
                "Multiply" => "Num *",
                "Add" => "Num +",
                "Seperator" => "Num Enter",
                "Subtract" => "Num −",
                "Decimal" => "Num .",
                "Divide" => "Num /",
                "Keypad 5/Clear" => "Num 5",
                "Ins" => "Insert",
                "Del" => "Delete",
                _ => name,
            };
        }

        /// <summary>As short as possible, for the controls summary: arrows become arrow signs.</summary>
        public static string Short(string original) => Friendly(original) switch
        {
            "Up arrow" => "↑",
            "Down arrow" => "↓",
            "Left arrow" => "←",
            "Right arrow" => "→",
            "Page Up" => "PgUp",
            "Page Down" => "PgDn",
            var other => other,
        };
    }

    /// <summary>The icon of a message dialog.</summary>
    internal enum DialogIcon
    {
        None,
        Info,
        Question,
        Warning,
        Error,
    }

    /// <summary>
    /// The game's modal dialogs, built in code in the application's compact Fluent style: Mica, a 32-pixel title bar,
    /// 12-point text, tight padding and small buttons.
    /// </summary>
    internal static partial class Dialogs
    {
        /// <summary>
        /// Developer hook (UI scripts): when set, dialogs are handed to it instead of being shown modally, so they can be
        /// rendered to pictures.
        /// </summary>
        internal static Action<FluentWindow>? Preview { get; set; }

        private static readonly Thickness BodyMargin = new(16, 10, 16, 14);

        private static FluentWindow NewWindow(Window? owner, string title, double width)
        {
            var window = new FluentWindow
            {
                Title = title,
                Width = width,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = owner == null,
                ExtendsContentIntoTitleBar = true,
                WindowBackdropType = WindowBackdropType.Mica,
                WindowCornerPreference = WindowCornerPreference.Round,
                FontSize = 12,
                MinWidth = 0,
                MinHeight = 0,
            };
            if (owner is { IsLoaded: true })
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            return window;
        }

        // Title bar, the body, and a right-aligned row of buttons under it.
        private static void SetContent(FluentWindow window, UIElement body, params System.Windows.Controls.Control[] buttons)
        {
            var root = new DockPanel { LastChildFill = true };
            var titleBar = new TitleBar { Title = window.Title, Height = 32, ShowMaximize = false, ShowMinimize = false };
            DockPanel.SetDock(titleBar, Dock.Top);
            root.Children.Add(titleBar);
            var panel = new StackPanel { Margin = BodyMargin };
            panel.Children.Add(body);
            if (buttons.Length > 0)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
                foreach (var b in buttons)
                {
                    b.MinWidth = 76;
                    b.Margin = new Thickness(6, 0, 0, 0);
                    row.Children.Add(b);
                }
                panel.Children.Add(row);
            }
            root.Children.Add(panel);
            window.Content = root;
        }

        /// <summary>The number of modal dialogs open now (the game holds while there is one).</summary>
        public static int OpenCount { get; private set; }

        /// <summary>Shows a window of its own as a modal dialog (the game holds meanwhile).</summary>
        public static bool? ShowModal(FluentWindow window) => Run(window);

        private static bool? Run(FluentWindow window)
        {
            if (Preview != null)
            {
                Preview(window);
                return null;
            }
            OpenCount++;
            try
            {
                return window.ShowDialog();
            }
            finally
            {
                OpenCount--;
            }
        }

        private static Button PrimaryButton(string text) => new() { Content = text, IsDefault = true, Appearance = ControlAppearance.Primary };

        private static Button SecondaryButton(string text, bool cancel = false) => new() { Content = text, IsCancel = cancel };

        private static TextBlock Text(string text, double bottom = 8) =>
            new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom) };

        private static TextBlock Dim(string text)
        {
            var t = new TextBlock { FontSize = 12, Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            return t;
        }

        private static TextBlock Heading(string text) =>
            new() { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };

        /// <summary>
        /// A message box: OK only, or Yes / No. Returns true for OK or Yes, false for No, null when closed.
        /// </summary>
        public static bool? Message(Window? owner, string text, string caption, DialogIcon icon = DialogIcon.Info, bool yesNo = false,
            string? okText = null, string noText = "No")
        {
            var window = NewWindow(owner, caption, 360);
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (icon != DialogIcon.None)
            {
                var symbol = new SymbolIcon
                {
                    Symbol = icon switch
                    {
                        DialogIcon.Question => SymbolRegular.QuestionCircle24,
                        DialogIcon.Warning => SymbolRegular.Warning24,
                        DialogIcon.Error => SymbolRegular.ErrorCircle24,
                        _ => SymbolRegular.Info24,
                    },
                    FontSize = 28,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
                symbol.SetResourceReference(Control.ForegroundProperty, icon switch
                {
                    DialogIcon.Warning => "SystemFillColorCautionBrush",
                    DialogIcon.Error => "SystemFillColorCriticalBrush",
                    _ => "AccentTextFillColorPrimaryBrush",
                });
                body.Children.Add(symbol);
            }
            var message = Text(text, 0);
            message.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(message, 1);
            body.Children.Add(message);

            bool? result = null;
            var ok = PrimaryButton(okText ?? (yesNo ? "Yes" : "OK"));
            ok.Click += (_, _) =>
            {
                result = true;
                window.DialogResult = true;
            };
            if (yesNo)
            {
                var no = SecondaryButton(noText, cancel: true);
                no.Click += (_, _) =>
                {
                    result = false;
                    window.DialogResult = false;
                };
                SetContent(window, body, ok, no);
            }
            else
            {
                ok.IsCancel = true;
                SetContent(window, body, ok);
            }
            window.Loaded += (_, _) => ok.Focus();
            Run(window);
            return result;
        }

        public static bool AskYesNo(Window? owner, string text, string caption) =>
            Message(owner, text, caption, DialogIcon.Question, yesNo: true) == true;

        /// <summary>A list to choose from (Game Select, Difficulty, Revert).</summary>
        public static int? Choose(Window? owner, string title, string prompt, IReadOnlyList<Choice> items, int selected, bool allowCancel = true)
        {
            var window = NewWindow(owner, title, 380);
            var list = new ListBox { MaxHeight = 300 };
            list.SetResourceReference(FrameworkElement.StyleProperty, "CompactList");
            list.ItemContainerStyle = Application.Current?.TryFindResource("CompactListItem") as Style;
            foreach (var item in items)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { FontSize = 12, Text = item.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                FrameworkElement? right = item.RatingMax > 0 ? Rating(item.Rating, item.RatingMax)
                    : !string.IsNullOrEmpty(item.Detail) ? Dim(item.Detail) : null;
                if (right != null)
                {
                    right.Margin = new Thickness(16, 0, 0, 0);
                    right.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(right, 1);
                    row.Children.Add(right);
                }
                list.Items.Add(row);
            }
            list.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, items.Count - 1));

            int? result = null;
            var ok = PrimaryButton("OK");
            ok.Click += (_, _) =>
            {
                result = list.SelectedIndex >= 0 ? list.SelectedIndex : null;
                window.DialogResult = result != null;
            };
            list.MouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListBoxItem)
                {
                    ok.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                }
            };
            var body = new StackPanel();
            body.Children.Add(Text(prompt));
            body.Children.Add(list);
            if (allowCancel)
            {
                SetContent(window, body, ok, SecondaryButton("Cancel", cancel: true));
            }
            else
            {
                SetContent(window, body, ok);
            }
            window.Loaded += (_, _) =>
            {
                list.Focus();
                if (list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) is ListBoxItem selectedItem)
                {
                    selectedItem.Focus();
                }
            };
            Run(window);
            return result;
        }

        public static int? ChooseDifficulty(Window? owner, IReadOnlyList<string> names, int current) =>
            Choose(owner, "Difficulty Selection", "Select the difficulty:",
                names.Select((n, i) => new Choice(n, Rating: i + 1, RatingMax: names.Count)).ToList(), current, allowCancel: false);

        // A row of small dots, the first ones filled.
        private static StackPanel Rating(int filled, int count)
        {
            var dots = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < count; i++)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0), StrokeThickness = 1 };
                if (i < filled)
                {
                    dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentTextFillColorPrimaryBrush");
                }
                else
                {
                    dot.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextFillColorTertiaryBrush");
                }
                dots.Children.Add(dot);
            }
            return dots;
        }

        /// <summary>A single line of text (the "Encoded Features" codeword dialog).</summary>
        public static string? AskText(Window? owner, string title, string prompt, int maxLength)
        {
            var window = NewWindow(owner, title, 320);
            var box = new TextBox { MaxLength = maxLength };
            string? result = null;
            var ok = PrimaryButton("OK");
            ok.Click += (_, _) =>
            {
                result = box.Text;
                window.DialogResult = true;
            };
            var body = new StackPanel();
            body.Children.Add(Text(prompt, 6));
            body.Children.Add(box);
            SetContent(window, body, ok, SecondaryButton("Cancel", cancel: true));
            window.Loaded += (_, _) => box.Focus();
            Run(window);
            return result;
        }

        /// <summary>Options > Update Speed: logic updates per second, 5 to 30.</summary>
        public static int? AskUpdateSpeed(Window? owner, int current)
        {
            var window = NewWindow(owner, "Update Speed", 340);
            var value = new TextBlock { FontSize = 12, Width = 28, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            var slider = new Slider { Minimum = 5, Maximum = 30, IsSnapToTickEnabled = true, TickFrequency = 1, Value = current, VerticalAlignment = VerticalAlignment.Center };
            value.Text = current.ToString(System.Globalization.CultureInfo.InvariantCulture);
            slider.ValueChanged += (_, e) => value.Text = ((int)e.NewValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
            int? result = null;
            var ok = PrimaryButton("OK");
            var defaults = SecondaryButton("Default");
            ok.Click += (_, _) =>
            {
                result = (int)slider.Value;
                window.DialogResult = true;
            };
            defaults.Click += (_, _) => slider.Value = 15;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(slider);
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            var body = new StackPanel();
            body.Children.Add(Text("Game updates per second.", 2));
            var hint = Dim("15 is the classic speed.");
            hint.Margin = new Thickness(0, 0, 0, 8);
            body.Children.Add(hint);
            body.Children.Add(row);
            SetContent(window, body, ok, defaults, SecondaryButton("Cancel", cancel: true));
            Run(window);
            return result;
        }

        // ---- Hall of Fame --------------------------------------------------------------------------------

        /// <summary>The Hall of Fame (HOFSCREEN): this player's scores against the best, level by level.</summary>
        public static void HallOfFame(Window? owner, ThexderGame game, int world, int level)
        {
            var window = NewWindow(owner, "Thexder Hall of Fame", 520);
            game.LoadLevelScores();
            var hof = game.LoadHallOfFame();
            int idx = Math.Clamp((world - 1) * 5 + (level - 1), 0, ThexderGame.LevelCount - 1);

            var previous = new Button { Icon = new SymbolIcon { Symbol = SymbolRegular.ChevronLeft20 }, ToolTip = "Previous level", Padding = new Thickness(6, 4, 6, 4) };
            var next = new Button { Icon = new SymbolIcon { Symbol = SymbolRegular.ChevronRight20 }, ToolTip = "Next level", Padding = new Thickness(6, 4, 6, 4) };
            var title = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            var header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(previous);
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            Grid.SetColumn(next, 2);
            header.Children.Add(next);
            var player = Dim("");
            player.HorizontalAlignment = HorizontalAlignment.Center;
            player.Margin = new Thickness(0, 0, 0, 10);

            var table = new Grid();
            string[] columns = { "", "Score", "Par", "Best", "Best player" };
            for (int c = 0; c < columns.Length; c++)
            {
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            }
            var cells = new TextBlock[3, 5];
            for (int r = 0; r < 3; r++)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < 5; c++)
                {
                    var t = r == 0 ? Dim(columns[c]) : new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                    t.Margin = new Thickness(c == 0 ? 0 : 16, 3, 0, 3);
                    t.TextAlignment = c is >= 1 and <= 3 ? TextAlignment.Right : TextAlignment.Left;
                    t.HorizontalAlignment = c is >= 1 and <= 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                    if (c == 0 && r > 0)
                    {
                        t.FontWeight = FontWeights.SemiBold;
                    }
                    Grid.SetRow(t, r);
                    Grid.SetColumn(t, c);
                    table.Children.Add(t);
                    cells[r, c] = t;
                }
            }
            cells[1, 0].Text = "Level";
            cells[2, 0].Text = "Cumulative";
            var frame = new Border { Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(6), Child = table };
            frame.SetResourceReference(FrameworkElement.StyleProperty, "Frame");

            void Fill()
            {
                int w = idx / 5 + 1, l = idx % 5 + 1;
                title.Text = $"{game.PlanetName(w)} · Level {l}";
                player.Text = string.IsNullOrEmpty(game.PlayerName) ? "" : $"Player: {game.PlayerName}";
                var (cumScore, cumPar) = game.Cumulative(idx);
                cells[1, 1].Text = game.LevelScores[idx].Score.ToString("N0");
                cells[1, 2].Text = game.LevelScores[idx].Par.ToString("N0");
                cells[1, 3].Text = hof[idx].LevelScore.ToString("N0");
                cells[1, 4].Text = hof[idx].LevelName;
                cells[2, 1].Text = cumScore.ToString("N0");
                cells[2, 2].Text = cumPar.ToString("N0");
                cells[2, 3].Text = hof[idx].CumulativeScore.ToString("N0");
                cells[2, 4].Text = hof[idx].CumulativeName;
                previous.IsEnabled = idx > 0;
                next.IsEnabled = idx < ThexderGame.LevelCount - 1;
            }
            previous.Click += (_, _) =>
            {
                idx--;
                Fill();
            };
            next.Click += (_, _) =>
            {
                idx++;
                Fill();
            };
            var close = PrimaryButton("Continue");
            close.IsCancel = true;
            close.Click += (_, _) => window.DialogResult = true;
            Fill();
            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(player);
            body.Children.Add(frame);
            SetContent(window, body, close);
            Run(window);
        }

        /// <summary>A text page (About).</summary>
        public static void ShowText(Window? owner, string title, string text)
        {
            var window = NewWindow(owner, title, 440);
            var close = PrimaryButton("OK");
            close.IsCancel = true;
            close.Click += (_, _) => window.DialogResult = true;
            SetContent(window, Text(text, 0), close);
            Run(window);
        }

        /// <summary>Converts a WPF key to a Windows virtual-key code.</summary>
        public static int VirtualKey(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            return KeyInterop.VirtualKeyFromKey(key);
        }
    }
}
