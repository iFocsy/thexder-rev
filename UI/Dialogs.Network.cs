using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using Thexder.Core.Game;
using Thexder.Services.Network;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;

namespace Thexder.UI
{
    /// <summary>What the Network Games dialog decided: create a game, or join the one at <see cref="Host"/>.</summary>
    internal sealed record NetChoice(bool Create, IPEndPoint? Host, string SessionName = "", NetGameParameters? Parameters = null, int Port = NetSession.DefaultPort);

    // The network dialogs, in the dialogs' compact style.
    internal static partial class Dialogs
    {
        /// <summary>File > Connect: the games on the local network, an address to join, or Create.</summary>
        public static NetChoice? NetworkGames(Window? owner, string playerName)
        {
            var window = NewWindow(owner, "Network Games", 460);
            var found = new List<NetSessionInfo>();
            var list = new ListBox { Height = 132 };
            list.SetResourceReference(FrameworkElement.StyleProperty, "CompactList");
            list.ItemContainerStyle = Application.Current?.TryFindResource("CompactListItem") as Style;
            var status = Dim("Searching the local network…");
            status.Margin = new Thickness(0, 4, 0, 0);
            var address = new TextBox { MaxLength = 100 };
            var refresh = new Button { Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowClockwise20 }, ToolTip = "Search again", Padding = new Thickness(6, 3, 6, 3) };

            async Task Search()
            {
                refresh.IsEnabled = false;
                status.Text = "Searching the local network…";
                IReadOnlyList<NetSessionInfo> games;
                try
                {
                    games = await LanDiscovery.FindAsync(NetSession.DefaultPort, TimeSpan.FromSeconds(1.5));
                }
                catch (SocketException)
                {
                    games = Array.Empty<NetSessionInfo>();
                }
                found.Clear();
                found.AddRange(games);
                list.Items.Clear();
                foreach (var g in found)
                {
                    list.Items.Add(ChoiceRow(new Choice(g.Name, $"{g.Details} · {g.Players}/{g.MaxPlayers}")));
                }
                if (found.Count > 0)
                {
                    list.SelectedIndex = 0;
                }
                status.Text = found.Count == 0 ? "No games found on this network. Create one, or join by address." : "";
                refresh.IsEnabled = true;
            }

            NetChoice? result = null;
            var create = SecondaryButton("Create…");
            var join = PrimaryButton("Join");
            create.Click += (_, _) =>
            {
                var created = CreateNetworkGame(window, playerName);
                if (created != null)
                {
                    result = created;
                    window.DialogResult = true;
                }
            };
            join.Click += async (_, _) =>
            {
                IPEndPoint? host = null;
                if (!string.IsNullOrWhiteSpace(address.Text))
                {
                    host = await ParseAddress(address.Text.Trim());
                    if (host == null)
                    {
                        Message(window, $"\"{address.Text.Trim()}\" is not a computer this one can find.", "Network Games", DialogIcon.Warning);
                        return;
                    }
                }
                else if (list.SelectedIndex >= 0 && list.SelectedIndex < found.Count)
                {
                    host = found[list.SelectedIndex].Host;
                }
                if (host == null)
                {
                    Message(window, "Select a game in the list, or type the address of the computer that created it.", "Network Games");
                    return;
                }
                result = new NetChoice(false, host);
                window.DialogResult = true;
            };
            list.MouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListBoxItem)
                {
                    address.Text = "";
                    join.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                }
            };
            refresh.Click += async (_, _) => await Search();

            var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var gamesLabel = Heading("Games on this network");
            gamesLabel.VerticalAlignment = VerticalAlignment.Center;
            gamesLabel.Margin = new Thickness(0);
            header.Children.Add(gamesLabel);
            Grid.SetColumn(refresh, 1);
            header.Children.Add(refresh);

            var body = new StackPanel();
            body.Children.Add(Text("Select the game you wish to play and click Join, or click Create to start a new game for you and others to play.", 10));
            body.Children.Add(header);
            body.Children.Add(list);
            body.Children.Add(status);
            body.Children.Add(Text("Or join by address (computer name or IP, optionally :port):", 4).WithMargin(0, 10, 0, 4));
            body.Children.Add(address);
            var notes = Dim("Your score is the hit points you make on the other players: the bigger the weapon, the higher the score. " +
                "Esc transports you to a random place in the level, for a penalty of 10 points.");
            notes.TextWrapping = TextWrapping.Wrap;
            notes.TextTrimming = TextTrimming.None;
            notes.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(notes);
            SetContent(window, body, create, join, SecondaryButton("Cancel", cancel: true));
            window.Loaded += async (_, _) => await Search();
            Run(window);
            return result;
        }

        /// <summary>Create Network Game (NETLOGON): the game's name and its six parameters.</summary>
        public static NetChoice? CreateNetworkGame(Window? owner, string playerName)
        {
            var window = NewWindow(owner, "Create Network Game", 380);
            var name = new TextBox { MaxLength = 31, Text = $"{playerName}'s game" };
            var gameNumber = Number(0, 32767, Random.Shared.Next(0x7fff));
            var enemies = Number(1, 30, 30);
            var createEnemies = new CheckBox { Content = "Create enemies", IsChecked = true };
            var regenEnemies = new CheckBox { Content = "Regenerate enemies", IsChecked = true };
            var regenObjects = new CheckBox { Content = "Regenerate objects", IsChecked = true };
            var scored = new CheckBox { Content = "Game ends at score", IsChecked = false };
            var limit = Number(100, 5000, 1000);
            limit.IsEnabled = false;
            scored.Checked += (_, _) => limit.IsEnabled = true;
            scored.Unchecked += (_, _) => limit.IsEnabled = false;
            createEnemies.Checked += (_, _) => enemies.IsEnabled = true;
            createEnemies.Unchecked += (_, _) => enemies.IsEnabled = false;
            var port = Number(1024, 65535, NetSession.DefaultPort);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            void Row(int r, UIElement left, UIElement? right)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (left is FrameworkElement fl)
                {
                    fl.VerticalAlignment = VerticalAlignment.Center;
                    fl.Margin = new Thickness(0, 3, 8, 3);
                }
                Grid.SetRow(left, r);
                grid.Children.Add(left);
                if (right is FrameworkElement fr)
                {
                    fr.Margin = new Thickness(0, 2, 0, 2);
                    Grid.SetRow(fr, r);
                    Grid.SetColumn(fr, 1);
                    grid.Children.Add(fr);
                }
            }
            Row(0, new TextBlock { FontSize = 12, Text = "Game number" }, gameNumber);
            Row(1, createEnemies, enemies);
            Row(2, regenEnemies, null);
            Row(3, regenObjects, null);
            Row(4, scored, limit);
            Row(5, new TextBlock { FontSize = 12, Text = "Port" }, port);
            var frame = new Border { Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(6), Child = grid, Margin = new Thickness(0, 10, 0, 0) };
            frame.SetResourceReference(FrameworkElement.StyleProperty, "Frame");

            NetChoice? result = null;
            var ok = PrimaryButton("Create");
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(name.Text))
                {
                    name.Focus();
                    return;
                }
                var parameters = new NetGameParameters(
                    (int)(gameNumber.Value ?? 0),
                    createEnemies.IsChecked == true ? (int)(enemies.Value ?? 30) : 0,
                    scored.IsChecked == true ? (int)(limit.Value ?? 1000) : 0,
                    createEnemies.IsChecked == true,
                    regenEnemies.IsChecked == true,
                    regenObjects.IsChecked == true);
                result = new NetChoice(true, null, name.Text.Trim(), parameters, (int)(port.Value ?? NetSession.DefaultPort));
                window.DialogResult = true;
            };
            var body = new StackPanel();
            body.Children.Add(Text("Name the game you wish to create:", 4));
            body.Children.Add(name);
            body.Children.Add(frame);
            SetContent(window, body, ok, SecondaryButton("Cancel", cancel: true));
            window.Loaded += (_, _) =>
            {
                name.Focus();
                name.SelectAll();
            };
            Run(window);
            return result;
        }

        private static NumberBox Number(int min, int max, int value) => new()
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            SmallChange = 1,
            LargeChange = 10,
            MaxDecimalPlaces = 0,
            ClearButtonEnabled = false,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        };

        // "host", "host:port", "1.2.3.4", "[::1]:port".
        private static async Task<IPEndPoint?> ParseAddress(string text)
        {
            if (IPEndPoint.TryParse(text, out var direct))
            {
                return direct.Port == 0 ? new IPEndPoint(direct.Address, NetSession.DefaultPort) : direct;
            }
            string host = text;
            int port = NetSession.DefaultPort;
            int colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text[(colon + 1)..], out int p) && p is > 0 and < 65536)
            {
                host = text[..colon];
                port = p;
            }
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);
                var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
                return address == null ? null : new IPEndPoint(address, port);
            }
            catch (SocketException)
            {
                return null;
            }
        }

        /// <summary>The waiting box of a joining player (NETJOINWAIT): modeless; Cancel leaves the game.</summary>
        public static FluentWindow JoinWait(Window owner, Action cancel)
        {
            var window = NewWindow(owner, "Thexder", 320);
            var button = SecondaryButton("Cancel", cancel: true);
            button.Click += (_, _) =>
            {
                cancel();
                window.Close();
            };
            var body = new StackPanel { Orientation = Orientation.Horizontal };
            body.Children.Add(new ProgressRing { IsIndeterminate = true, Width = 24, Height = 24, Margin = new Thickness(0, 0, 12, 0) });
            body.Children.Add(new TextBlock { FontSize = 12, Text = "Waiting for setup information from the host.\nPlease wait.", VerticalAlignment = VerticalAlignment.Center });
            SetContent(window, body, button);
            return window;
        }

        // A choice-list row (title, and a dimmed detail on the right).
        private static Grid ChoiceRow(Choice item)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { FontSize = 12, Text = item.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(item.Detail))
            {
                var detail = Dim(item.Detail);
                detail.Margin = new Thickness(16, 0, 0, 0);
                Grid.SetColumn(detail, 1);
                row.Children.Add(detail);
            }
            return row;
        }

        private static T WithMargin<T>(this T element, double left, double top, double right, double bottom) where T : FrameworkElement
        {
            element.Margin = new Thickness(left, top, right, bottom);
            return element;
        }
    }
}
