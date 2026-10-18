using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Thexder.UI
{
    /// <summary>Closing only hides it; clicks are reported in surface pixels.</summary>
    internal partial class PanelWindow
    {
        public PanelWindow(string title, int surfaceWidth, int surfaceHeight, Thickness pictureMargin)
        {
            InitializeComponent();
            Title = title;
            TitleBar.Title = title;
            SurfaceWidth = surfaceWidth;
            SurfaceHeight = surfaceHeight;
            Picture.Margin = pictureMargin;
            Closing += OnClosing;
            SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        }

        /// <summary>The user started dragging or resizing the window (the system move/size loop).</summary>
        public event Action? MoveSizeStarted;

        /// <summary>The window is being dragged (WM_MOVING), before the move takes effect.</summary>
        public event Action? Moving;

        /// <summary>The move/size loop ended; true when the window was resized rather than moved.</summary>
        public event Action<bool>? MoveSizeEnded;

        /// <summary>True between <see cref="MoveSizeStarted"/> and <see cref="MoveSizeEnded"/>.</summary>
        public bool InMoveSize { get; private set; }

        /// <summary>True while the move/size loop is resizing the window (not moving it).</summary>
        public bool IsResizing => InMoveSize && resized;

        private bool resized;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232, WM_SIZING = 0x0214, WM_MOVING = 0x0216;
            switch (msg)
            {
                case WM_ENTERSIZEMOVE:
                    InMoveSize = true;
                    resized = false;
                    MoveSizeStarted?.Invoke();
                    break;
                case WM_SIZING:
                    resized = true;
                    break;
                case WM_MOVING:
                    Moving?.Invoke();
                    break;
                case WM_EXITSIZEMOVE:
                    InMoveSize = false;
                    MoveSizeEnded?.Invoke(resized);
                    break;
            }
            return IntPtr.Zero;
        }

        public int SurfaceWidth { get; }

        public int SurfaceHeight { get; }

        /// <summary>Height of the title bar, added to the picture height when sizing the window.</summary>
        public static double TitleHeight => 28;

        /// <summary>Left or right click at a surface position; the bool is true for a double click.</summary>
        public event Action<int, int, bool>? LeftClick;

        public event Action<int, int>? RightClick;

        /// <summary>The user closed the window (it is only hidden).</summary>
        public event Action? HiddenByUser;

        public void SetPicture(ImageSource? source)
        {
            if (!ReferenceEquals(Picture.Source, source))
            {
                Picture.Source = source;
            }
        }

        public void SetTitle(string title)
        {
            if (TitleBar.Title as string != title)
            {
                TitleBar.Title = title;
                Title = title;
            }
        }

        private (int X, int Y) SurfacePoint(MouseEventArgs e)
        {
            var p = e.GetPosition(Picture);
            return ((int)(p.X * SurfaceWidth / Math.Max(1, Picture.ActualWidth)), (int)(p.Y * SurfaceHeight / Math.Max(1, Picture.ActualHeight)));
        }

        private void OnMouseLeft(object sender, MouseButtonEventArgs e)
        {
            var (x, y) = SurfacePoint(e);
            LeftClick?.Invoke(x, y, e.ClickCount >= 2);
        }

        private void OnMouseRight(object sender, MouseButtonEventArgs e)
        {
            var (x, y) = SurfacePoint(e);
            RightClick?.Invoke(x, y);
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (Owner is { IsLoaded: true } && !AllowClose)
            {
                e.Cancel = true;
                Hide();
                HiddenByUser?.Invoke();
            }
        }

        /// <summary>Set when the main window closes, so the panels really close.</summary>
        public bool AllowClose { get; set; }
    }
}
