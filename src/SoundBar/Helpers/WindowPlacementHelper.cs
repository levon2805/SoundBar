using System;
using Windows.Graphics;

namespace SoundBar.Helpers
{
    /// <summary>
    /// Keeps the window somewhere the user can actually see it when it's restored.
    /// </summary>
    public static class WindowPlacementHelper
    {
        /// <summary>
        /// When a window's minimised, Windows sneakily parks it at around (-32000, -32000).
        /// Nothing that far out is ever a real position.
        /// </summary>
        private const int MinimisedPositionThreshold = -30000;

        /// <summary>
        /// True if the position looks like the off-screen spot Windows parks minimised windows at.
        /// </summary>
        public static bool IsMinimisedPosition(double left, double top) =>
            left <= MinimisedPositionThreshold || top <= MinimisedPositionThreshold;

        /// <summary>
        /// Shrinks and nudges a window rectangle so it fits fully inside a monitor's work area.
        /// </summary>
        public static RectInt32 ClampToWorkArea(RectInt32 window, RectInt32 workArea)
        {
            int width = Math.Clamp(window.Width, 1, Math.Max(1, workArea.Width));
            int height = Math.Clamp(window.Height, 1, Math.Max(1, workArea.Height));

            int x = Math.Clamp(window.X, workArea.X, workArea.X + workArea.Width - width);
            int y = Math.Clamp(window.Y, workArea.Y, workArea.Y + workArea.Height - height);

            return new RectInt32(x, y, width, height);
        }
    }
}
