namespace SoundBarTaskbar
{
    /// <summary>
    /// One thing on the taskbar (a button, the clock, a tray icon), as reported by Windows' accessibility
    /// interface. The rectangle is relative to the taskbar's top-left corner.
    /// </summary>
    internal readonly record struct TaskbarElement(Native.RECT Rect, string ClassName, string AutomationId);

    /// <summary>
    /// Works out where the indicator fits on a taskbar. It's pure maths with no Windows calls, so it's easy to test.
    /// </summary>
    internal static class TaskbarLayout
    {
        // Big containers that hold the buttons rather than being buttons themselves.
        private static readonly HashSet<string> ContainerClasses = new(StringComparer.Ordinal)
        {
            "Windows.UI.Input.InputSite.WindowClass", "Taskbar.TaskbarFrameAutomationPeer",
            "Windows.UI.Composition.DesktopWindowContentBridge", "ReBarWindow32", "MSTaskSwWClass",
            "MSTaskListWClass", "WorkerW", "TrayNotifyWnd", "SysPager", "ToolbarWindow32",
            IndicatorWindow.ClassName,
        };

        // The tray on the right: the ^ button, tray icons, network and volume, the clock and Show Desktop.
        private static readonly HashSet<string> TrayClasses = new(StringComparer.Ordinal)
        {
            "TrayNotifyWnd", "TrayClockWClass", "TrayShowDesktopButtonWClass", "TrayButton", "TrayInputIndicatorWClass",
        };

        // Start, Search, Task View and the app buttons. Nothing is placed to the left of these.
        private static readonly HashSet<string> CoreAutomationIds = new(StringComparer.Ordinal)
        {
            "StartButton", "SearchButton", "TaskViewButton",
        };

        private static bool IsTray(TaskbarElement e) =>
            e.AutomationId == "SystemTrayIcon" || TrayClasses.Contains(e.ClassName);

        private static bool IsCore(TaskbarElement e) =>
            CoreAutomationIds.Contains(e.AutomationId) || e.ClassName.Contains("TaskListButton", StringComparison.Ordinal);

        /// <summary>
        /// The x position (relative to the taskbar) for an indicator <paramref name="width"/> pixels wide, or null
        /// if it doesn't fit without covering something. It sits just left of the tray, in the rightmost free gap.
        /// </summary>
        /// <param name="fallbackTrayLeft">Where the tray starts if the scan found no tray items (for example the
        /// TrayNotifyWnd window on the main taskbar). Use the taskbar's width when there's nothing better.</param>
        /// <param name="margin">The space to keep between the indicator and its neighbours.</param>
        /// <param name="spare">How much free room is left beside the indicator (0 if it doesn't fit). Small means a
        /// new app button could soon reach it, so it's worth checking more often.</param>
        public static int? FindSlot(int taskbarWidth, int taskbarHeight, IReadOnlyList<TaskbarElement> elements,
            int fallbackTrayLeft, int width, int margin, out int spare)
        {
            spare = 0;
            // Vertical taskbars (Windows 10 can put the taskbar on the left or right) aren't supported.
            if (width <= 0 || taskbarWidth <= 0 || taskbarHeight <= 0 || taskbarWidth <= taskbarHeight) return null;

            bool Usable(TaskbarElement e) =>
                e.Rect.Width > 0 && e.Rect.Height > 0 && e.Rect.Right > 0 && e.Rect.Left < taskbarWidth;

            int trayLeft = int.MaxValue;
            foreach (var e in elements)
            {
                if (Usable(e) && IsTray(e) && e.Rect.Left > 0)
                    trayLeft = Math.Min(trayLeft, e.Rect.Left);
            }
            if (trayLeft == int.MaxValue)
                trayLeft = fallbackTrayLeft > 0 && fallbackTrayLeft <= taskbarWidth ? fallbackTrayLeft : taskbarWidth;

            int coreRight = 0;
            foreach (var e in elements)
            {
                if (Usable(e) && IsCore(e) && e.Rect.Right <= trayLeft)
                    coreRight = Math.Max(coreRight, e.Rect.Right);
            }

            // Everything else between the app buttons and the tray (the Widgets button, for example) is in the way.
            var obstacles = new List<Native.RECT>();
            foreach (var e in elements)
            {
                if (!Usable(e) || IsTray(e) || IsCore(e) || ContainerClasses.Contains(e.ClassName)) continue;
                if (e.Rect.Width >= taskbarWidth / 2) continue;
                if (e.Rect.Right <= coreRight || e.Rect.Left >= trayLeft) continue;
                obstacles.Add(e.Rect);
            }
            obstacles.Sort((a, b) => b.Right.CompareTo(a.Right));

            int lowerBound = coreRight + margin;
            int gapRight = trayLeft - margin;
            foreach (var o in obstacles)
            {
                if (o.Right + margin <= gapRight)
                {
                    // The free gap between this obstacle and the last one (or the tray)
                    int room = gapRight - Math.Max(o.Right + margin, lowerBound);
                    if (room >= width)
                    {
                        spare = room - width;
                        return gapRight - width;
                    }
                }
                gapRight = Math.Min(gapRight, o.Left - margin);
                if (gapRight <= lowerBound) return null;
            }

            if (gapRight - lowerBound < width) return null;
            spare = gapRight - lowerBound - width;
            return gapRight - width;
        }
    }
}
