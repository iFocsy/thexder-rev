using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Thexder.Core.Assets;
using Thexder.Core.Game;
using Thexder.Feature;

namespace Thexder.UI
{
    // Help > Controls and Options > Remap Keys: the current keys as keycaps - an eight-way pad for moving, the three
    // forms with their sprites, combat and arsenal, and the fixed system keys. In remap mode a keycap is clicked and the
    // next key pressed takes its place (a key already in use swaps over). Templates: the old-school keys or modern WASD.
    internal partial class ControlsWindow
    {
        private static readonly string[] ActionNames =
        {
            "Fire", "Swap walker weapons", "Jet", "Tank", "Walker", "Explode next bomb",
            "Up / jump", "Down", "Left", "Right", "Up-left", "Up-right", "Down-left", "Down-right",
            "Next weapon", "Previous weapon", "Previous equipment", "Next equipment", "Use equipment",
        };

        private const string Tips = "Laser, Fireballs and Fan-Fire aim themselves; grenades and missiles fire the way you hold. " +
            "Click an item in the Equipment window twice to switch it on. The jet arrives in world 2, the tank in world 3.";

        private static readonly Brush Brand = Solid(0xE8, 0x46, 0x3A);
        private static readonly Brush LaserBlue = Solid(0x4F, 0xD8, 0xFF);
        private static readonly Brush Light = Solid(0xEE, 0xF1, 0xF8);
        private static readonly Brush Dim = Solid(0x8F, 0x98, 0xAE);
        private static readonly Brush CapEdge = Solid(0x47, 0x53, 0x7F);
        private static readonly Brush CardEdge = Solid(0x20, 0x28, 0x4A);
        private static readonly Brush CapFill = Vertical(Color.FromRgb(0x2C, 0x34, 0x58), Color.FromRgb(0x15, 0x1A, 0x30));
        private static readonly Brush CardFill = Vertical(Color.FromRgb(0x11, 0x17, 0x2E), Color.FromRgb(0x0B, 0x0F, 0x20));

        private readonly int[] original;
        private readonly int[] bindings;
        private readonly Func<int, string> keyName;
        private readonly bool startedInView;
        private readonly BitmapSource? walker, jet, tank;
        private readonly Dictionary<int, (Border Cap, TextBlock Label)> caps = new();
        private readonly Dictionary<int, TextBlock> captions = new();
        private bool editing;
        private int capturing = -1;

        public ControlsWindow(GameArt art, Palette palette, int[] bindings, Func<int, string> keyName, bool remap)
        {
            InitializeComponent();
            original = (int[])bindings.Clone();
            this.bindings = (int[])bindings.Clone();
            this.keyName = keyName;
            startedInView = !remap;
            int s = art.S;
            walker = SpaceScene.Frames(art.Sprite("ThexderR"), 4 * s, 6 * s, 1, palette, first: 12).FirstOrDefault();
            jet = SpaceScene.Frames(art.Sprite("Plane"), 5 * s, 5 * s, 1, palette).FirstOrDefault();
            tank = SpaceScene.Frames(art.Sprite("ThexderTankR"), 4 * s, 3 * s, 1, palette).FirstOrDefault();
            PreviewKeyDown += OnPreviewKeyDown;
            SetMode(remap);
        }

        /// <summary>The keys saved with Save, or null.</summary>
        public int[]? Result { get; private set; }

        private void SetMode(bool edit)
        {
            editing = edit;
            capturing = -1;
            Title = WindowTitle.Title = edit ? "Remap Keys" : "Thexder Controls";
            Heading.Text = Spaced(edit ? "Remap Keys" : "Controls");
            RemapButton.Visibility = edit ? Visibility.Collapsed : Visibility.Visible;
            SaveButton.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
            CloseButton.Content = edit ? "Cancel" : "Close";
            Status.Text = edit ? "Click a key, then press the new key for it. A key already in use swaps over." : Tips;
            Templates.Children.Clear();
            if (edit)
            {
                Templates.Children.Add(Chip("Old School", () => Apply(KeyLayouts.OldSchool, "Old School: the keys of the 1995 game - arrows and keypad, Space fires.")));
                Templates.Children.Add(Chip("Modern", () => Apply(KeyLayouts.Modern, "Modern: WASD to move with Q E Z C diagonals, Space fires, 1 2 3 change form, J L weapons, U O equipment, K uses it.")));
                Templates.Children.Add(Chip("Custom", null));
            }
            Build();
        }

        // ---- Layout ----------------------------------------------------------------------------------------------

        private void Build()
        {
            caps.Clear();
            captions.Clear();
            Body.Children.Clear();
            Body.ColumnDefinitions.Clear();
            Body.RowDefinitions.Clear();
            Body.ColumnDefinitions.Add(new ColumnDefinition());
            Body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            Body.ColumnDefinitions.Add(new ColumnDefinition());
            Body.RowDefinitions.Add(new RowDefinition());
            Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(Card("Movement", MovementPad()));
            left.Children.Add(Card("Transform", Forms()));
            var right = new StackPanel();
            right.Children.Add(Card("Combat", Rows(0, 5, 1)));
            right.Children.Add(Card("Arsenal", Rows(15, 14, 16, 17, 18)));
            Grid.SetColumn(right, 2);
            var system = Card("System", SystemKeys());
            Grid.SetRow(system, 1);
            Grid.SetColumnSpan(system, 3);
            Body.Children.Add(left);
            Body.Children.Add(right);
            Body.Children.Add(system);
            Refresh();
        }

        private static Border Card(string title, UIElement content)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = Spaced(title), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = LaserBlue, Margin = new Thickness(2, 0, 0, 8) });
            stack.Children.Add(content);
            return new Border
            {
                Background = CardFill,
                BorderBrush = CardEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 12),
                Margin = new Thickness(0, 0, 0, 12),
                Child = stack,
            };
        }

        // The eight directions around the walker, as they lie on a keypad.
        private UIElement MovementPad()
        {
            var pad = new Grid();
            for (int i = 0; i < 3; i++)
            {
                pad.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                pad.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            (int Action, string Arrow)?[] cells =
            {
                (10, "↖"), (6, "↑"), (11, "↗"),
                (8, "←"), null, (9, "→"),
                (12, "↙"), (7, "↓"), (13, "↘"),
            };
            for (int i = 0; i < cells.Length; i++)
            {
                UIElement cell = cells[i] is { } c ? Keycap(c.Action, 70, 54, c.Arrow) : Sprite(walker, 70, 54);
                Grid.SetRow(cell, i / 3);
                Grid.SetColumn(cell, i % 3);
                pad.Children.Add(cell);
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(pad);
            row.Children.Add(new TextBlock
            {
                Text = "Up jumps with the walker.\n\nThe jet flies all eight ways; the tank drives left and right.",
                Width = 120,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
                Foreground = Dim,
            });
            return row;
        }

        private UIElement Forms()
        {
            var grid = new UniformGrid { Columns = 3 };
            foreach (var (action, name, picture) in new[] { (4, "Walker", walker), (2, "Jet", jet), (3, "Tank", tank) })
            {
                var tile = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                tile.Children.Add(Sprite(picture, 86, 58));
                tile.Children.Add(new TextBlock { Text = name, FontSize = 11.5, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) });
                tile.Children.Add(Keycap(action, 86, 36));
                grid.Children.Add(tile);
            }
            return grid;
        }

        private UIElement Rows(params int[] actions)
        {
            var stack = new StackPanel();
            foreach (int action in actions)
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var cap = Keycap(action, 104, 32);
                DockPanel.SetDock(cap, Dock.Right);
                row.Children.Add(cap);
                row.Children.Add(new TextBlock { Text = ActionNames[action], FontSize = 12.5, Foreground = Light, VerticalAlignment = VerticalAlignment.Center });
                stack.Children.Add(row);
            }
            return stack;
        }

        private static UIElement SystemKeys()
        {
            var wrap = new WrapPanel();
            foreach (var (key, what) in new[] { ("Ctrl+P", "Pause"), ("F1", "Controls"), ("F3", "Frame rate"), ("F11", "Full screen"), ("Enter", "Continue"), ("Esc", "Network teleport") })
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
                var label = new TextBlock { Text = key, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Light, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                item.Children.Add(CapBorder(double.NaN, 28, label, new Thickness(8, 0, 8, 0)));
                item.Children.Add(new TextBlock { Text = what, FontSize = 11.5, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                wrap.Children.Add(item);
            }
            return wrap;
        }

        private static UIElement Sprite(BitmapSource? picture, double width, double height)
        {
            var image = new Image { Source = picture, Width = width, Height = height, Stretch = Stretch.Uniform, Margin = new Thickness(3) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            return image;
        }

        private Border Keycap(int action, double width, double height, string? caption = null)
        {
            var label = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Light, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(label);
            if (caption != null)
            {
                var small = new TextBlock { Text = caption, FontSize = 11, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center };
                stack.Children.Add(small);
                captions[action] = small;
            }
            var cap = CapBorder(width, height, stack, new Thickness(4, 0, 4, 0));
            if (editing)
            {
                cap.Cursor = Cursors.Hand;
                cap.ToolTip = $"Change the key for {ActionNames[action]}";
                cap.MouseLeftButtonUp += (_, _) => BeginCapture(action);
                cap.MouseEnter += (_, _) => { if (capturing != action) cap.BorderBrush = LaserBlue; };
                cap.MouseLeave += (_, _) => { if (capturing != action) cap.BorderBrush = CapEdge; };
            }
            caps[action] = (cap, label);
            return cap;
        }

        private static Border CapBorder(double width, double height, UIElement child, Thickness padding) => new()
        {
            Width = width,
            Height = height,
            Padding = padding,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1, 1, 1, 3),
            Background = CapFill,
            BorderBrush = CapEdge,
            Child = child,
            SnapsToDevicePixels = true,
        };

        // A template chip in the header; a null action makes the "Custom" marker.
        private Border Chip(string text, Action? apply)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(15),
                Padding = new Thickness(14, 5, 14, 6),
                Margin = new Thickness(8, 0, 0, 0),
                BorderThickness = new Thickness(1),
                Tag = text,
                Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Light },
            };
            if (apply != null)
            {
                chip.Cursor = Cursors.Hand;
                chip.MouseLeftButtonUp += (_, _) => apply();
            }
            return chip;
        }

        private void Refresh()
        {
            foreach (var (action, (cap, label)) in caps)
            {
                label.Text = action == capturing ? "press a key" : KeyLabel(bindings[action]);
                label.FontSize = action == capturing ? 11 : 13.5;
                cap.BorderBrush = action == capturing ? Brand : CapEdge;
                if (captions.TryGetValue(action, out var small))
                {
                    small.Visibility = small.Text == label.Text ? Visibility.Collapsed : Visibility.Visible; // an arrow key needs no arrow under it
                }
            }
            string? layout = KeyLayouts.NameOf(bindings);
            Subheading.Text = editing ? $"Layout: {layout ?? "Custom"}" : $"Your keys · {layout ?? "Custom"} layout";
            foreach (Border chip in Templates.Children)
            {
                bool on = (string)chip.Tag == (layout ?? "Custom");
                chip.Background = on ? Brand : Solid(0x16, 0x1C, 0x38);
                chip.BorderBrush = on ? Solid(0xFF, 0x8A, 0x78) : CapEdge;
                chip.Opacity = (string)chip.Tag == "Custom" && !on ? 0.45 : 1;
            }
        }

        private string KeyLabel(int index) => index >= 0 && index < GameController.KeyTable.Length ? KeyNames.Short(keyName(index)) : "?";

        // ---- Remapping -------------------------------------------------------------------------------------------

        private void Apply(int[] layout, string message)
        {
            EndCapture(message);
            layout.CopyTo(bindings, 0);
            Refresh();
        }

        private void BeginCapture(int action)
        {
            EndCapture(Status.Text);
            capturing = action;
            Refresh();
            var pulse = new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(420)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            caps[action].Cap.BeginAnimation(OpacityProperty, pulse);
            Status.Text = $"Press the new key for {ActionNames[action]}. Esc cancels.";
            Keyboard.Focus(this);
        }

        private void EndCapture(string message)
        {
            if (capturing >= 0 && caps.TryGetValue(capturing, out var current))
            {
                current.Cap.BeginAnimation(OpacityProperty, null);
            }
            capturing = -1;
            Status.Text = message;
            Refresh();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (capturing < 0)
            {
                return;
            }
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                EndCapture("Unchanged.");
                return;
            }
            int index = Array.IndexOf(GameController.KeyTable, GameController.NumLockOff(Dialogs.VirtualKey(e)));
            if (index < 0)
            {
                Status.Text = $"{key} cannot be used in Thexder. Press another key for {ActionNames[capturing]}, or Esc.";
                return;
            }
            int action = capturing, other = Array.IndexOf(bindings, index);
            string message = $"{ActionNames[action]}: {KeyLabel(index)}.";
            if (other >= 0 && other != action)
            {
                bindings[other] = bindings[action];
                message += $" {ActionNames[other]} moved to {KeyLabel(bindings[other])}.";
            }
            bindings[action] = index;
            EndCapture(message);
            if (other >= 0 && other != action)
            {
                Flash(caps[other].Cap);
            }
        }

        // The keycap of an action that got a new key in a swap glows gold for a moment.
        private static void Flash(Border cap)
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47));
            cap.BorderBrush = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromRgb(0x47, 0x53, 0x7F), TimeSpan.FromMilliseconds(1600)) { BeginTime = TimeSpan.FromMilliseconds(500) });
        }

        private void OnRemap(object sender, RoutedEventArgs e) => SetMode(true);

        private void OnSave(object sender, RoutedEventArgs e)
        {
            Result = (int[])bindings.Clone();
            DialogResult = true;
        }

        // Cancel in remap mode goes back to the controls when the window opened on them.
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            if (editing && startedInView)
            {
                original.CopyTo(bindings, 0);
                SetMode(false);
                return;
            }
            DialogResult = false;
        }

        // ---- Helpers ---------------------------------------------------------------------------------------------

        private static string Spaced(string text) =>
            string.Join("  ", text.ToUpperInvariant().Split(' ').Select(word => string.Join(" ", word.ToCharArray())));

        private static SolidColorBrush Solid(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush Vertical(Color top, Color bottom)
        {
            var brush = new LinearGradientBrush(top, bottom, 90);
            brush.Freeze();
            return brush;
        }
    }
}
