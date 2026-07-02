using System;
using System.Runtime.InteropServices;

namespace SoundBar.Helpers
{
    public static class NativeMethods
    {
        // Win32 Constants
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        // Point structure for screen coordinates
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        // Retrieves the cursor's position, in screen coordinates
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        // Sends the specified message to a window
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        // Releases the mouse capture from a window
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
    }
}
