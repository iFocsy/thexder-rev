using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Thexder.Core.Game;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace Thexder.UI
{
    /// <summary>
    /// The Network Communicator (NETCOMM): the players with their colours and scores, best first, a message to one player
    /// or to all, and the messages received (newest on top, at most 30).
    /// </summary>
    internal sealed class NetCommWindow : FluentWindow
    {
        // Screen colours that stand for the players' tint tables (NetColourNames order).
        private static readonly Color[] Swatches =
        {
            Color.FromRgb(0xf0, 0xf0, 0xf0), Color.FromRgb(0xff, 0xd2, 0x4a), Color.FromRgb(0x9b, 0xe3, 0x6d),
            Color.FromRgb(0x4f, 0xd3, 0xe6), Color.FromRgb(0xe0, 0x5b, 0xd8), Color.FromRgb(0x5a, 0xa7, 0xe8),
            Color.FromRgb(0x47, 0xb8, 0x5a), Color.FromRgb(0x4a, 0x62, 0xe8), Color.FromRgb(0xa5, 0x5b, 0xe8),
            Color.FromRgb(0x2e, 0x7d, 0x32),
        };

        private readonly StackPanel players = new();
        private readonly ComboBox to = new() { MinWidth = 110 };
        private readonly TextBox message = new() { MaxLength = 80 };
        private readonly ListBox messages = new() { Height = 96 };
        private readonly List<int> targets = new();

        public NetCommWindow(Window owner)
        {
            Owner = owner;
            Title = "Network Communicator";
            Width = 300;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            ExtendsContentIntoTitleBar = true;
            WindowBackdropType = WindowBackdropType.Mica;
            FontSize = 12;
            MinWidth = 0;
            MinHeight = 0;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var send = new Button { Content = "Send", Appearance = ControlAppearance.Primary, Margin = new Thickness(6, 0, 0, 0) };
            send.Click += (_, _) => DoSend();
            message.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    DoSend();
                    e.Handled = true;
                }
            };
            messages.SetResourceReference(StyleProperty, "CompactList");
            messages.ItemContainerStyle = Application.Current?.TryFindResource("CompactListItem") as Style;

            var playerFrame = new Border { Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(6), Child = players };
            playerFrame.SetResourceReference(StyleProperty, "Frame");

            var line = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(message);
            Grid.SetColumn(send, 1);
            line.Children.Add(send);

            var toRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            toRow.Children.Add(new TextBlock { FontSize = 12, Text = "Message to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            toRow.Children.Add(to);

            var body = new StackPanel { Margin = new Thickness(12, 8, 12, 12) };
            body.Children.Add(Heading("Players"));
            body.Children.Add(playerFrame);
            body.Children.Add(toRow);
            body.Children.Add(line);
            var received = Heading("Messages");
            received.Margin = new Thickness(0, 10, 0, 4);
            body.Children.Add(received);
            body.Children.Add(messages);

            var root = new DockPanel();
            var titleBar = new TitleBar { Title = Title, Height = 28, ShowMaximize = false, ShowMinimize = false };
            DockPanel.SetDock(titleBar, Dock.Top);
            root.Children.Add(titleBar);
            root.Children.Add(body);
            Content = root;
            Closing += (_, e) =>
            {
                if (!AllowClose)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        /// <summary>The Send button: (player id or 0 for all, text).</summary>
        public event Action<int, string>? Send;

        /// <summary>Set when the network game ends, so the window really closes.</summary>
        public bool AllowClose { get; set; }

        /// <summary>Redraws the player list and the recipients (NetCommRefresh).</summary>
        public void Refresh(IReadOnlyList<NetScore> scores)
        {
            players.Children.Clear();
            foreach (var s in scores)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var swatch = new Rectangle
                {
                    Width = 10,
                    Height = 10,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = new SolidColorBrush(Swatches[Math.Clamp(s.Colour, 0, Swatches.Length - 1)]),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = ThexderGame.NetColourNames[Math.Clamp(s.Colour, 0, ThexderGame.NetColourNames.Length - 1)],
                };
                var name = new TextBlock { FontSize = 12, Text = s.Local ? $"{s.Name} (you)" : s.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                if (s.Local)
                {
                    name.FontWeight = FontWeights.SemiBold;
                }
                var score = new TextBlock { FontSize = 12, Text = s.Score.ToString("N0"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(name, 1);
                Grid.SetColumn(score, 2);
                row.Children.Add(swatch);
                row.Children.Add(name);
                row.Children.Add(score);
                players.Children.Add(row);
            }

            int selected = to.SelectedIndex >= 0 && to.SelectedIndex < targets.Count ? targets[to.SelectedIndex] : 0;
            targets.Clear();
            to.Items.Clear();
            targets.Add(0);
            to.Items.Add("All Players");
            foreach (var s in scores.Where(s => !s.Local))
            {
                targets.Add(s.Id);
                to.Items.Add(s.Name);
            }
            to.SelectedIndex = Math.Max(0, targets.IndexOf(selected));
        }

        /// <summary>A received message, newest on top, at most 30.</summary>
        public void AddMessage(string from, string text)
        {
            messages.Items.Insert(0, new TextBlock { FontSize = 12, Text = $"{from}: {text}", TextWrapping = TextWrapping.Wrap });
            while (messages.Items.Count > 30)
            {
                messages.Items.RemoveAt(messages.Items.Count - 1);
            }
        }

        private void DoSend()
        {
            string text = message.Text.Trim();
            if (text.Length == 0)
            {
                return;
            }
            int target = to.SelectedIndex >= 0 && to.SelectedIndex < targets.Count ? targets[to.SelectedIndex] : 0;
            Send?.Invoke(target, text);
            AddMessage("You", text);
            message.Text = "";
            Owner?.Activate();
        }

        private static TextBlock Heading(string text) =>
            new() { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
    }
}
