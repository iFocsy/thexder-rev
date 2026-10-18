using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Thexder.UI
{
    // WPF-UI 4.3's title bar answers a window's edges as resize borders whatever its ResizeMode, so a NoResize window
    // could still be dragged to another size.
    internal static class FixedSizeWindows
    {
        private const int WmNcHitTest = 0x0084;
        private const int HtClient = 1;
        private const int HtCaption = 2;

        public static void Register() =>
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window && FindTitleBar(window) is { } titleBar)
            {
                titleBar.WndProcInvoked += (_, args) => HitTest(window, titleBar, args);
            }
        }

        private static void HitTest(Window window, TitleBar titleBar, HwndProcEventArgs args)
        {
            if (args.Message != WmNcHitTest || window.ResizeMode != ResizeMode.NoResize || !GetWindowRect(args.HWND, out NativeRect rect))
            {
                return;
            }

            // The system's sizing frame, which the title bar treats as the resize band, in device pixels.
            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            Thickness band = SystemParameters.WindowResizeBorderThickness;
            long point = args.LParam.ToInt64();
            int x = (short)(point & 0xFFFF);
            int y = (short)((point >> 16) & 0xFFFF);
            bool top = y < rect.Top + band.Top * dpi.DpiScaleY;
            if (top || x < rect.Left + band.Left * dpi.DpiScaleX || x >= rect.Right - band.Right * dpi.DpiScaleX || y >= rect.Bottom - band.Bottom * dpi.DpiScaleY)
            {
                args.ReturnValue = top && titleBar.IsVisible ? HtCaption : HtClient;
                args.Handled = true;
            }
        }

        private static TitleBar? FindTitleBar(DependencyObject root)
        {
            var queue = new Queue<DependencyObject>(new[] { root });
            while (queue.Count > 0)
            {
                DependencyObject node = queue.Dequeue();
                if (node is TitleBar titleBar)
                {
                    return titleBar;
                }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    queue.Enqueue(VisualTreeHelper.GetChild(node, i));
                }
            }
            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    }
}
