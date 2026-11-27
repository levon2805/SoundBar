using System;
using System.Diagnostics;
using System.Linq;
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

        /// <summary>
        /// A Windows key is held too. Those presses belong to Windows' own shortcuts, so they never match ours.
        /// </summary>
        public bool WindowsKeyHeld { get; }

        /// <summary>
        /// The keys are AltGr plus a key that types a character on this keyboard layout, like AltGr+M for µ on a
        /// German keyboard. Windows reports AltGr as Ctrl+Alt, so without this a Ctrl+Alt hotkey would eat it.
        /// </summary>
        public bool TypesAltGrCharacter { get; }

        public HotkeyEventArgs(VirtualKey key, HotkeyModifiers modifiers, bool windowsKeyHeld = false, bool typesAltGrCharacter = false)
        {
            Key = key;
            Modifiers = modifiers;
            WindowsKeyHeld = windowsKeyHeld;
            TypesAltGrCharacter = typesAltGrCharacter;
            Handled = false;
        }

        /// <summary>
        /// True when this key press is someone typing or using a Windows shortcut, so it mustn't trigger a hotkey.
        /// </summary>
        public bool IsNotForHotkeys => WindowsKeyHeld || TypesAltGrCharacter;
    }

    /// <summary>
    /// Which key combinations make sensible hotkeys.
    /// </summary>
    public static class HotkeyRules
    {
        /// <summary>
        /// A hotkey needs Ctrl or Alt, otherwise it would swallow that key whenever you type in an app in the
        /// mixer (a hotkey of just "M" would stop you typing m). F13 to F24 are allowed alone: no keyboard
        /// types with them, so they're only ever used for shortcuts.
        /// </summary>
        /// <param name="hotkey">As SoundBar writes it, like "Control+Alt+M".</param>
        public static bool IsAllowed(string? hotkey)
        {
            if (string.IsNullOrWhiteSpace(hotkey)) return false;
            string[] parts = hotkey.Split('+');
            string key = parts[^1];
            if (key.Length == 0) return false;

            bool hasCtrlOrAlt = parts[..^1].Any(p => p == "Control" || p == "Alt");
            if (hasCtrlOrAlt) return true;

            return key.Length >= 3 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out int n) && n >= 13 && n <= 24;
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
                try
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    VirtualKey key = (VirtualKey)vkCode;

                    // Don't raise events for modifier keys themselves to avoid noise
                    if (key != VirtualKey.Control && key != VirtualKey.LeftControl && key != VirtualKey.RightControl &&
                        key != VirtualKey.Menu && key != VirtualKey.LeftMenu && key != VirtualKey.RightMenu &&
                        key != VirtualKey.Shift && key != VirtualKey.LeftShift && key != VirtualKey.RightShift &&
                        key != VirtualKey.LeftWindows && key != VirtualKey.RightWindows)
                    {
                        var mods = GetModifiers();
                        bool windowsKey = IsDown(VK_LWIN) || IsDown(VK_RWIN);

                        // AltGr is the right Alt key on layouts that have it. Only look up the character
                        // when it's held with Ctrl+Alt, which is rare, so ordinary typing stays cheap.
                        bool altGrCharacter = mods.HasFlag(HotkeyModifiers.Control) && mods.HasFlag(HotkeyModifiers.Alt) &&
                            IsDown(VK_RMENU) && TypesCharacterWithCtrlAlt((uint)vkCode, (uint)Marshal.ReadInt32(lParam, 4), mods.HasFlag(HotkeyModifiers.Shift));

                        var args = new HotkeyEventArgs(key, mods, windowsKey, altGrCharacter);
                        KeyPressed?.Invoke(this, args);

                        if (args.Handled)
                        {
                            return (IntPtr)1; // Suppress the key
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Hotkey hook error: {ex.Message}");
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_RMENU = 0xA5;

        private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>
        /// Whether Ctrl+Alt (or AltGr) plus this key types a character in the app in front, using that app's
        /// keyboard layout. This doesn't disturb half-typed accents, so it's safe to ask mid-typing.
        /// </summary>
        private static bool TypesCharacterWithCtrlAlt(uint vk, uint scanCode, bool shift)
        {
            try
            {
                uint thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                IntPtr layout = GetKeyboardLayout(thread);

                var state = new byte[256];
                state[0x11] = state[0xA2] = 0x80; // Ctrl, left Ctrl (AltGr sends this)
                state[0x12] = state[VK_RMENU] = 0x80; // Alt, right Alt
                if (shift) state[0x10] = 0x80;

                var buffer = new char[8];
                // Flag 4: leave the keyboard state alone (Windows 10 1607 and later)
                return ToUnicodeEx(vk, scanCode, state, buffer, buffer.Length, 4, layout) != 0; // -1 is an accent key, still typing
            }
            catch
            {
                return false;
            }
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ToUnicodeEx(uint virtKey, uint scanCode, byte[] keyState,
            [Out, MarshalAs(UnmanagedType.LPArray)] char[] buffer, int bufferSize, uint flags, IntPtr layout);
    }
}
