using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SoundBar.Shared;

namespace SoundBar.Services
{
    /// <summary>
    /// Turns the taskbar info (SoundBarTaskbar.exe, which sits next to SoundBar.exe) on and off.
    ///
    /// "On" simply means its startup shortcut exists and points at this copy's helper, so the helper starts
    /// with Windows by itself, whether or not SoundBar does. Nothing else stores it, so SoundBar and the helper
    /// (which removes the shortcut when you pick "Hide from Taskbar") can never disagree.
    /// </summary>
    public class TaskbarHelperService
    {
        private const uint WM_CLOSE = 0x0010;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowW(string? className, string? windowName);

        [DllImport("user32.dll")]
        private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private readonly string _appFolder;
        private readonly string? _startupFolder;
        private readonly Func<bool> _isRunning;
        private readonly Action<string> _start;
        private readonly Action _stop;

        /// <param name="appFolder">The folder SoundBar.exe is in. Tests pass a scratch folder.</param>
        /// <param name="startupFolder">The Startup folder, or null for the real one. Tests pass a scratch folder.</param>
        /// <param name="isRunning">Tests replace how the helper is found, started and stopped.</param>
        internal TaskbarHelperService(string? appFolder = null, string? startupFolder = null,
            Func<bool>? isRunning = null, Action<string>? start = null, Action? stop = null)
        {
            _appFolder = appFolder ?? AppContext.BaseDirectory;
            _startupFolder = startupFolder;
            _isRunning = isRunning ?? (() => FindHostWindow() != IntPtr.Zero);
            _start = start ?? StartProcess;
            _stop = stop ?? CloseRunningHelper;
        }

        public TaskbarHelperService() : this(null) { }

        /// <summary>This copy's helper program.</summary>
        public string HelperPath => Path.Combine(_appFolder, TaskbarHelperInfo.ExeName);

        private string ShortcutPath => TaskbarHelperInfo.GetStartupShortcutPath(_startupFolder);

        /// <summary>False if the helper isn't next to SoundBar.exe (an incomplete copy), so it can't be turned on.</summary>
        public bool IsAvailable => File.Exists(HelperPath);

        /// <summary>Whether the taskbar info is turned on for this copy of SoundBar.</summary>
        public bool IsEnabled
        {
            get
            {
                try
                {
                    if (!File.Exists(ShortcutPath)) return false;
                    return TaskbarHelperInfo.SamePath(ShortcutFile.ReadTarget(ShortcutPath), HelperPath);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Whether any copy of the helper is running right now.</summary>
        public bool IsRunning => _isRunning();

        /// <summary>Turns the taskbar info on: starts it now and every time you sign in.</summary>
        /// <returns>False if the helper is missing, so nothing was turned on.</returns>
        public bool Enable()
        {
            if (!IsAvailable) return false;
            try
            {
                ShortcutFile.Save(ShortcutPath, HelperPath, _appFolder, "Shows your volume and microphone on the taskbar");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Couldn't create the taskbar startup shortcut: {ex.Message}");
            }
            if (!IsRunning) _start(HelperPath);
            return true;
        }

        /// <summary>
        /// Turns the taskbar info off: closes it and stops it starting with Windows. A shortcut that belongs to
        /// another copy of SoundBar (say the installed one, while a test build runs) is left alone.
        /// </summary>
        public void Disable()
        {
            try
            {
                if (File.Exists(ShortcutPath))
                {
                    string? target = ShortcutFile.ReadTarget(ShortcutPath);
                    if (string.IsNullOrEmpty(target) || !File.Exists(target) || TaskbarHelperInfo.SamePath(target, HelperPath))
                        File.Delete(ShortcutPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Couldn't remove the taskbar startup shortcut: {ex.Message}");
            }
            if (IsRunning) _stop();
        }

        /// <summary>
        /// Starts the helper if it's turned on but not running (it crashed, or was closed from Task Manager).
        /// Called when SoundBar opens.
        /// </summary>
        public void EnsureRunningIfEnabled()
        {
            if (IsAvailable && IsEnabled && !IsRunning) _start(HelperPath);
        }

        /// <summary>
        /// Whether to turn the taskbar info on and tell the user about it. Anyone who hasn't seen it yet gets it
        /// once, whichever version they updated from (or if they've just installed SoundBar).
        /// </summary>
        internal static bool ShouldOfferTaskbarInfo(bool hasSeenOffer, bool helperAvailable) => !hasSeenOffer && helperAvailable;

        /// <summary>The helper's hidden window, if it's running.</summary>
        internal static IntPtr FindHostWindow() => FindWindowW(TaskbarHelperInfo.HostWindowClass, null);

        /// <summary>
        /// Asks a running helper to close. It's a polite request (WM_CLOSE), so the helper tidies up first.
        /// </summary>
        internal static void CloseRunningHelper()
        {
            IntPtr host = FindHostWindow();
            if (host != IntPtr.Zero) PostMessageW(host, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        private void StartProcess(string path)
        {
            try
            {
                using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = _appFolder });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Couldn't start the taskbar helper: {ex.Message}");
            }
        }
    }
}
