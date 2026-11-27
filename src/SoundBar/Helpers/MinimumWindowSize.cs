using System;
using System.Runtime.InteropServices;

namespace SoundBar.Helpers
{
    /// <summary>
    /// Stops the window being dragged narrower than SoundBar's layout can handle. Much thinner than that and the
    /// title, media buttons and version number end up on top of each other.
    /// </summary>
    /// <remarks>
    /// Windows asks a window for its smallest size (WM_GETMINMAXINFO) whenever it's resized. This version of the
    /// Windows App SDK has no setting for it, so we answer that question ourselves.
    /// </remarks>
    public sealed class MinimumWindowSize : IDisposable
    {
        /// <summary>
        /// The narrowest the window can go, at 100% display scaling. Windows counts its invisible 7 px resize border
        /// on each side, so the window you see is 330 wide. Any narrower and the mute button (just right of the
        /// centred play button) runs into the version number in the corner.
        /// </summary>
        public const int MinWidth = 344;

        private const uint WM_GETMINMAXINFO = 0x0024;
        private static readonly UIntPtr SubclassId = (UIntPtr)0x53424D57; // "SBMW"

        private readonly IntPtr _hWnd;
        private readonly SubclassProc _proc; // Kept alive for as long as Windows can call it
        private bool _attached;

        public MinimumWindowSize(IntPtr hWnd)
        {
            _hWnd = hWnd;
            _proc = WindowProc;
            _attached = SetWindowSubclass(hWnd, _proc, SubclassId, UIntPtr.Zero);
        }

        /// <summary>
        /// The narrowest the window can go in real pixels, at the given display scaling (96 is 100%).
        /// </summary>
        internal static int MinWidthInPixels(uint dpi) => (int)Math.Ceiling(MinWidth * (dpi == 0 ? 96 : dpi) / 96.0);

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
        {
            if (msg == WM_GETMINMAXINFO && lParam != IntPtr.Zero)
            {
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                int min = MinWidthInPixels(NativeMethods.GetDpiForWindow(hWnd));
                if (info.ptMinTrackSize.X < min)
                {
                    info.ptMinTrackSize.X = min;
                    Marshal.StructureToPtr(info, lParam, false);
                }
            }
            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_attached)
            {
                RemoveWindowSubclass(_hWnd, _proc, SubclassId);
                _attached = false;
            }
            GC.KeepAlive(_proc);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr data);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
