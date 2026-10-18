using System.Net.Sockets;
using System.Windows;
using Thexder.Core.Game;
using Wpf.Ui.Controls;

namespace Thexder.UI
{
    // Network play in the window: File > Connect / Disconnect, the join waiting box, the Network Communicator, and what
    // happens when a network game ends.
    public partial class MainWindow
    {
        private NetCommWindow? netComm;
        private FluentWindow? joinWait;
        private bool netEnding;

        private void InitNetwork()
        {
            Game.NetStarted += () => Dispatcher.BeginInvoke(OnNetStarted);
            Game.NetEnded += reason => Dispatcher.BeginInvoke(() => ShowNetDisconnected());
            controller.Net.Lost += reason => Dispatcher.BeginInvoke(() => ShowNetDisconnected());
            Game.NetGameWon += winner => Dispatcher.BeginInvoke(() => OnNetWon(winner));
            Game.NetChatReceived += (name, text) => netComm?.AddMessage(name, text);
            Game.NetPlayersChanged += () => netComm?.Refresh(Game.NetScores());
        }

        private async void OnConnect(object sender, RoutedEventArgs e)
        {
            if (!Game.SessionActive || Game.NetActive || controller.Kiosk)
            {
                return;
            }
            Game.Input.ReleaseAll();
            var choice = Dialogs.NetworkGames(this, Game.PlayerName);
            if (choice == null)
            {
                return;
            }
            if (choice.Create)
            {
                try
                {
                    controller.Net.Host(choice.SessionName, choice.Parameters!, choice.Port);
                }
                catch (SocketException ex)
                {
                    Dialogs.Message(this, $"The game could not be created on port {choice.Port}:\n{ex.Message}", "Network Games", DialogIcon.Error);
                    return;
                }
                ShowNetComm();
                SyncMenus();
                return;
            }

            joinWait = Dialogs.JoinWait(this, () =>
            {
                joinWait = null;
                controller.Net.Leave(); // Back to the single-player game, which waited
                SyncMenus();
            });
            joinWait.Show();
            try
            {
                await controller.Net.JoinAsync(choice.Host!);
            }
            catch (Exception ex) when (ex is SocketException or InvalidOperationException or InvalidDataException or IOException or OperationCanceledException)
            {
                CloseJoinWait();
                Dialogs.Message(this, $"Could not join the game at {choice.Host}:\n{ex.Message}", "Network Games", DialogIcon.Warning);
            }
            SyncMenus();
        }

        private void OnDisconnect(object sender, RoutedEventArgs e)
        {
            if (!Game.NetActive)
            {
                return;
            }
            controller.Net.Leave();
            ShowNetDisconnected();
        }

        private void OnNetComm(object sender, RoutedEventArgs e)
        {
            if (Game.NetActive)
            {
                ShowNetComm();
            }
        }

        private void OnNetStarted()
        {
            CloseJoinWait();
            ShowNetComm();
            SyncMenus();
        }

        // "Game over, X has won.": the machine that notices ends the game (the host ends it for everybody).
        private void OnNetWon(string winner)
        {
            if (!Game.NetActive || netEnding)
            {
                return;
            }
            Dialogs.Message(this, $"Game over,\n{winner} has won.", "Thexder", DialogIcon.Warning);
            controller.Net.Leave();
            ShowNetDisconnected();
        }

        // Continue with the single-player game, or leave Thexder.
        private void ShowNetDisconnected()
        {
            if (netEnding)
            {
                return;
            }
            netEnding = true;
            try
            {
                CloseJoinWait();
                if (netComm != null)
                {
                    netComm.AllowClose = true;
                    netComm.Close();
                    netComm = null;
                }
                SyncMenus();
                if (Dialogs.AskYesNo(this, "Network game disconnected.\nDo you wish to continue playing your single player game?\n'No' will exit the program.", "Thexder"))
                {
                    Game.NetContinueSinglePlayer();
                    SyncMenus();
                }
                else
                {
                    closingConfirmed = true;
                    Close();
                }
            }
            finally
            {
                netEnding = false;
            }
        }

        private void ShowNetComm()
        {
            if (netComm == null)
            {
                netComm = new NetCommWindow(this);
                netComm.Send += (to, text) => Game.NetSendChat(to, text);
                // Right of the main window when there is room, else left of the side column, else over the playfield.
                var work = MonitorArea.Of(this).Work;
                double left = Left + ActualWidth + 4;
                if (left + netComm.Width > work.Right)
                {
                    left = Left - dock.ColumnWidth - netComm.Width - 4;
                }
                if (left < work.Left)
                {
                    left = Left + ActualWidth - netComm.Width - 12;
                }
                netComm.Left = left;
                netComm.Top = Top + 40;
            }
            netComm.Refresh(Game.NetScores());
            netComm.Show();
        }

        private void CloseJoinWait()
        {
            var window = joinWait;
            joinWait = null;
            window?.Close();
        }
    }
}
