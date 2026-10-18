using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Thexder.Utils;

namespace Thexder.UI
{
    /// <summary>
    /// The monitor a window is on: its work area in WPF units, its size in pixels and the pixels the taskbar takes.
    /// </summary>
    internal readonly record struct MonitorArea(Rect Work, int PixelWidth, int PixelHeight, int ReservedWidth, int ReservedHeight, double Scale)
    {
        public static MonitorArea Of(Window window)
        {
            double scale = Math.Max(0.5, VisualTreeHelper.GetDpi(window).DpiScaleX);
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
            var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                var work = SystemParameters.WorkArea;
                int w = (int)Math.Round(SystemParameters.PrimaryScreenWidth * scale);
                int h = (int)Math.Round(SystemParameters.PrimaryScreenHeight * scale);
                return new MonitorArea(work, w, h, w - (int)(work.Width * scale), h - (int)(work.Height * scale), scale);
            }
            var m = info.Monitor;
            var r = info.Work;
            return new MonitorArea(
                new Rect(r.Left / scale, r.Top / scale, (r.Right - r.Left) / scale, (r.Bottom - r.Top) / scale),
                m.Right - m.Left,
                m.Bottom - m.Top,
                (m.Right - m.Left) - (r.Right - r.Left),
                (m.Bottom - m.Top) - (r.Bottom - r.Top),
                scale);
        }

        /// <summary>True when a monitor of this pixel size is not larger than this one.</summary>
        public bool Holds(int pixelWidth, int pixelHeight) => pixelWidth <= PixelWidth && pixelHeight <= PixelHeight;

        /// <summary>
        /// The work area a monitor of the given pixel size would offer (same taskbar), centred on this one.
        /// </summary>
        public Rect WorkFor(int pixelWidth, int pixelHeight)
        {
            double w = Math.Min(Work.Width, (pixelWidth - ReservedWidth) / Scale);
            double h = Math.Min(Work.Height, (pixelHeight - ReservedHeight) / Scale);
            return new Rect(Work.Left + (Work.Width - w) / 2, Work.Top + (Work.Height - h) / 2, w, h);
        }
    }
}
