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
    }
}
