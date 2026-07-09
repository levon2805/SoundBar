using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SoundBar.Helpers
{
    /// <summary>
    /// Provides helper methods to interact with Win32 Window APIs.
    /// Used primarily to detect which app the user is currently focused on.
    /// </summary>
    public static class WindowHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        /// <summary>
        /// Gets the Process ID of the application that currently has user focus.
        /// </summary>
        /// <returns>The Process ID, or 0 if it could not be determined.</returns>
        public static uint GetForegroundProcessId()
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
            {
                return 0;
            }

            GetWindowThreadProcessId(hWnd, out uint processId);
            return processId;
        }

        /// <summary>
        /// Gets the title of the currently focused window.
        /// </summary>
        public static string GetForegroundWindowTitle()
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
                return string.Empty;

            int length = GetWindowTextLength(hWnd);
            if (length == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
