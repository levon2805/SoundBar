using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.System;

namespace SoundBar.Services
{
    [Flags]
    public enum HotkeyModifiers
    {
        None = 0,
        Control = 1,
        Alt = 2,
        Shift = 4
    }

    public class HotkeyEventArgs : EventArgs
    {
        public VirtualKey Key { get; }
        public HotkeyModifiers Modifiers { get; }
        public bool Handled { get; set; }

        public HotkeyEventArgs(VirtualKey key, HotkeyModifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
            Handled = false;
        }
    }

    /// <summary>
    /// Implements a global low-level keyboard hook to listen for hotkeys even when the app is in the background.
    /// </summary>
    public class HotkeyService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        public event EventHandler<HotkeyEventArgs>? KeyPressed;

        public HotkeyService()
        {
            _proc = HookCallback;
            _hookID = SetHook(_proc);
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            try
            {
                IntPtr hInstance = Marshal.GetHINSTANCE(typeof(HotkeyService).Module);
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, hInstance, 0);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                VirtualKey key = (VirtualKey)vkCode;

                // Don't raise events for modifier keys themselves to avoid noise
                if (key != VirtualKey.Control && key != VirtualKey.LeftControl && key != VirtualKey.RightControl &&
                    key != VirtualKey.Menu && key != VirtualKey.LeftMenu && key != VirtualKey.RightMenu &&
                    key != VirtualKey.Shift && key != VirtualKey.LeftShift && key != VirtualKey.RightShift)
                {
                    var args = new HotkeyEventArgs(key, GetModifiers());
                    KeyPressed?.Invoke(this, args);

                    if (args.Handled)
                    {
                        return (IntPtr)1; // Suppress the key
                    }
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private HotkeyModifiers GetModifiers()
        {
            HotkeyModifiers mods = HotkeyModifiers.None;
            if ((GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= HotkeyModifiers.Control; // VK_CONTROL
            if ((GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= HotkeyModifiers.Alt; // VK_MENU
            if ((GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= HotkeyModifiers.Shift; // VK_SHIFT
            return mods;
        }

        public void Dispose()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
            GC.KeepAlive(_proc);
        }

        // --- P/Invoke Definitions ---

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
