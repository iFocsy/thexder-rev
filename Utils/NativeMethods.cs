using System.Runtime.InteropServices;

namespace Thexder.Utils
{
    internal static partial class NativeMethods
    {
        public const int AttachParentProcess = -1;

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AttachConsole(int processId);

        // 1 ms timer resolution so Thread.Sleep based pacing (music sequencer, frame pacing) is precise.
        [LibraryImport("winmm.dll")]
        public static partial uint timeBeginPeriod(uint period);

        [LibraryImport("winmm.dll")]
        public static partial uint timeEndPeriod(uint period);

        public const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [LibraryImport("user32.dll")]
        public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        public static partial IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll")]
        public static partial IntPtr GetFocus();

        [LibraryImport("user32.dll")]
        public static partial IntPtr GetActiveWindow();

        [LibraryImport("user32.dll")]
        public static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
        public static partial uint MapVirtualKey(uint code, uint mapType);

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }
}
