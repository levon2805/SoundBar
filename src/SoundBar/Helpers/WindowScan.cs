using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SoundBar.Helpers
{
    /// <summary>
    /// Finds which processes have a window, in one pass over all the windows on the PC.
    /// </summary>
    public static class WindowScan
    {
        /// <summary>
        /// The IDs of every process with a main window: a visible window that doesn't belong to another window.
        /// That's the same rule .NET uses for Process.MainWindowHandle, without walking every window once per process.
        /// </summary>
        public static HashSet<int> ProcessIdsWithMainWindow()
        {
            var ids = new HashSet<int>();
            EnumWindows((hWnd, _) =>
            {
                if (IsWindowVisible(hWnd) && GetWindow(hWnd, GW_OWNER) == IntPtr.Zero)
                {
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid != 0) ids.Add((int)pid);
                }
                return true; // Keep going
            }, IntPtr.Zero);
            return ids;
        }

        /// <summary>
        /// An app counts as running in the background when none of its processes has a main window.
        /// </summary>
        public static bool IsBackground(IEnumerable<int> processIds, ISet<int> withWindows) =>
            !processIds.Any(withWindows.Contains);

        private const uint GW_OWNER = 4;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
