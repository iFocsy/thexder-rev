using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Thexder.Core.Assets;
using Thexder.Core.Game;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;

namespace Thexder.UI
{
    // The main menu over the whole stage: pick a profile (or name a new one) and play; Controls, Remap Keys, About and
    // Exit below. Enter plays, a double click on a profile too; the bin on a profile (or Delete) asks to remove it.
    internal partial class TitleMenu
    {
        public TitleMenu()
        {
            InitializeComponent();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Play();
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete && Profiles.IsKeyboardFocusWithin && Profiles.SelectedItem is ListBoxItem { Tag: string name })
                {
                    DeleteRequested?.Invoke(name);
                    e.Handled = true;
                }
            };
        }

        public event Action<string>? PlayRequested;

        public event Action<string>? DeleteRequested;

        public event Action? ControlsRequested;

        public event Action? RemapRequested;

        public event Action? AboutRequested;

        public event Action? ExitRequested;

        public bool IsOpen => Visibility == Visibility.Visible;

        private bool held, still;

        /// <summary>Holds the scene while a dialog is open over the menu.</summary>
        public bool Paused
        {
            get => held;
            set
            {
                held = value;
                Scene.Paused = held || still;
            }
        }

        /// <summary>The player's choice with the button at the top right: the scene animates or stands still.</summary>
        public bool Animated
        {
            get => !still;
            set
            {
                still = !value;
                Scene.Paused = held || still;
                AnimationIcon.Symbol = still ? Wpf.Ui.Controls.SymbolRegular.Play24 : Wpf.Ui.Controls.SymbolRegular.Pause24;
                AnimationButton.ToolTip = still ? "Play the animation" : "Pause the animation";
            }
        }

        public event Action<bool>? AnimatedChanged;

        private void OnToggleAnimation(object sender, RoutedEventArgs e)
        {
            Animated = !Animated;
            AnimatedChanged?.Invoke(Animated);
        }

        /// <summary>The profile to play: a new name typed in, or the one selected.</summary>
        public string? ChosenName => NewProfile.Text.Trim() is { Length: > 0 } typed ? typed : (Profiles.SelectedItem as ListBoxItem)?.Tag as string;

        /// <summary>Opens the menu with the known profiles (name and a line about their game) and the last one selected.</summary>
        public void Open(GameArt art, Palette palette, IReadOnlyList<(string Name, string Detail)> profiles, string last)
        {
            Scene.Start(art, palette);
            Profiles.Items.Clear();
            foreach (var (name, detail) in profiles)
            {
                Profiles.Items.Add(new ListBoxItem { Content = ProfileCard(name, detail), Tag = name, Padding = new Thickness(8, 6, 8, 6), Style = (Style)Resources["ProfileItem"] });
            }
            Profiles.Visibility = profiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoProfiles.Visibility = profiles.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            NewProfile.Text = "";
            int index = profiles.Select(p => p.Name).ToList().FindIndex(n => n.Equals(last, StringComparison.OrdinalIgnoreCase));
            if (profiles.Count > 0)
            {
                Profiles.SelectedIndex = Math.Max(0, index);
            }
            else
            {
                NewProfile.Text = last.Length > 0 ? last : Environment.UserName;
            }
            UpdatePlayText();
            Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(FocusMenu, DispatcherPriority.ApplicationIdle);
        }

        public void Close() => Visibility = Visibility.Collapsed;

        public void FocusMenu()
        {
            if (Profiles.SelectedItem is ListBoxItem item)
            {
                item.Focus();
            }
            else
            {
                NewProfile.Focus();
                NewProfile.SelectAll();
            }
        }

        public void Play()
        {
            if (ChosenName is { } name)
            {
                PlayRequested?.Invoke(name);
            }
            else
            {
                NewProfile.Focus();
            }
        }

        // An avatar with the profile's initial, the name, where their game stands, and a bin to delete it.
        private UIElement ProfileCard(string name, string detail)
        {
            var avatar = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0x6A, 0x50), Color.FromRgb(0x8A, 0x1A, 0x12), 60),
                Child = new TextBlock
                {
                    Text = name[..1].ToUpperInvariant(),
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF8)) });
            text.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xAE)), TextTrimming = TextTrimming.CharacterEllipsis });
            var delete = new Wpf.Ui.Controls.Button
            {
                Icon = new SymbolIcon(SymbolRegular.Delete20),
                Appearance = ControlAppearance.Transparent,
                Padding = new Thickness(6),
                MinHeight = 0,
                Opacity = 0.45,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Delete this profile",
            };
            delete.MouseEnter += (_, _) => delete.Opacity = 1;
            delete.MouseLeave += (_, _) => delete.Opacity = 0.45;
            delete.Click += (_, e) =>
            {
                e.Handled = true;
                DeleteRequested?.Invoke(name);
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(text, 1);
            Grid.SetColumn(delete, 2);
            row.Children.Add(avatar);
            row.Children.Add(text);
            row.Children.Add(delete);
            return row;
        }

        private void UpdatePlayText() =>
            PlayText.Text = NewProfile.Text.Trim().Length > 0 ? "New profile · Play" : ChosenName is { } name ? $"Play as {name}" : "Play";

        private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
        {
            if (Profiles.SelectedIndex >= 0 && NewProfile.Text.Length > 0)
            {
                NewProfile.Text = "";
            }
            UpdatePlayText();
        }

        private void OnNewProfileChanged(object sender, TextChangedEventArgs e)
        {
            if (NewProfile.Text.Trim().Length > 0 && Profiles.SelectedIndex >= 0)
            {
                Profiles.SelectedIndex = -1;
            }
            UpdatePlayText();
        }

        private void OnProfileDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Profiles.SelectedItem != null)
            {
                Play();
            }
        }

        private void OnPlay(object sender, RoutedEventArgs e) => Play();

        private void OnControls(object sender, RoutedEventArgs e) => ControlsRequested?.Invoke();

        private void OnRemapKeys(object sender, RoutedEventArgs e) => RemapRequested?.Invoke();

        private void OnAbout(object sender, RoutedEventArgs e) => AboutRequested?.Invoke();

        private void OnExit(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
    }
}
