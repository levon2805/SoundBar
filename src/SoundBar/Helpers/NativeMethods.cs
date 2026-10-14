using System;
using System.Runtime.InteropServices;

namespace SoundBar.Helpers
{
    /// <summary>
    /// A collection of dark magic P/Invoke methods to talk directly to the Windows API.
    /// Mostly used here so we can drag the borderless window around by its background.
    /// </summary>
    public static class NativeMethods
    {
        /// <summary>
        /// A Windows message telling the system that the left mouse button is pressed on a non-client area.
        /// </summary>
        public const int WM_NCLBUTTONDOWN = 0xA1;

        /// <summary>
        /// A hit-test code telling Windows that the mouse is over the title bar (caption) of the window.
        /// </summary>
        public const int HT_CAPTION = 0x2;

        /// <summary>
        /// A simple coordinate structure used by the Windows API.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// Asks Windows exactly where the mouse cursor is sitting on the screen right now.
        /// </summary>
        /// <param name="lpPoint">The point structure to fill with the coordinates.</param>
        /// <returns>True if it successfully grabbed the position.</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        /// <summary>
        /// Sends a message directly to a specific window's message queue.
        /// We use this to trick Windows into thinking we clicked the title bar.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        /// <summary>
        /// Tells Windows to stop letting our app hog all the mouse input.
        /// Crucial for letting the drag operation transition smoothly to the OS.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        // --- Always on Top ---

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_LAYERED = 0x00080000;

        /// <summary>
        /// Shuffles a window up or down the pile of open windows. We use it to pin SoundBar on top by
        /// talking to Windows directly, because WinUI's IsAlwaysOnTop can get quietly ignored while
        /// the app's still starting up.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        // --- Window Opacity ---

        public const uint LWA_ALPHA = 0x00000002;

        /// <summary>
        /// Sets how see-through a layered window is. Alpha goes from 0 (completely invisible) to 255 (totally solid).
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        // --- Window Icon ---

        public const int WM_SETICON = 0x0080;
        public const int ICON_SMALL = 0;
        public const int ICON_BIG = 1;
        public const uint IMAGE_ICON = 1;
        public const uint LR_LOADFROMFILE = 0x0010;
        public const int SM_CXICON = 11;
        public const int SM_CYICON = 12;
        public const int SM_CXSMICON = 49;
        public const int SM_CYSMICON = 50;

        /// <summary>
        /// The resource ID .NET gives the &lt;ApplicationIcon&gt; when it bakes it into the exe.
        /// </summary>
        public const int AppIconResourceId = 32512;

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        /// <summary>
        /// Loads an icon from a resource inside a module (pass the resource ID as the name).
        /// </summary>
        [DllImport("user32.dll", EntryPoint = "LoadImageW", SetLastError = true)]
        public static extern IntPtr LoadImage(IntPtr hInst, IntPtr name, uint type, int cx, int cy, uint fuLoad);

        /// <summary>
        /// Loads an icon from a file on disk (use with LR_LOADFROMFILE).
        /// </summary>
        [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    }
}
