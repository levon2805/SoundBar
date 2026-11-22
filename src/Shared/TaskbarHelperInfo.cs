using System;
using System.IO;

namespace SoundBar.Shared
{
    /// <summary>
    /// The names SoundBar and its taskbar helper (SoundBarTaskbar.exe) both rely on. This one file is compiled
    /// into both programs, so they can never disagree about them.
    /// </summary>
    internal static class TaskbarHelperInfo
    {
        /// <summary>The helper's program file, which sits next to SoundBar.exe.</summary>
        public const string ExeName = "SoundBarTaskbar.exe";

        /// <summary>The image name Windows lists the helper under (for tasklist and taskkill).</summary>
        public const string ImageName = "SoundBarTaskbar.exe";

        /// <summary>
        /// The class of the helper's hidden main window. SoundBar finds it by this name to ask the helper to
        /// close (it posts WM_CLOSE, so the helper tidies up properly).
        /// </summary>
        public const string HostWindowClass = "SoundBarTaskbarHost";

        /// <summary>Stops a second copy of the helper from starting.</summary>
        public const string MutexName = @"Local\SoundBarTaskbar";

        /// <summary>
        /// The shortcut in the Startup folder that starts the helper when you sign in. Whether it exists is
        /// what "the taskbar info is turned on" means, so nothing else needs to store that.
        /// </summary>
        public const string StartupShortcutName = "SoundBar Taskbar.lnk";

        /// <summary>Where the helper remembers which monitor it's on. Only the helper writes it.</summary>
        public static string SettingsFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundBar", "taskbar.json");

        /// <summary>The helper's startup shortcut. Tests pass a scratch folder so they never touch the real one.</summary>
        public static string GetStartupShortcutPath(string? startupFolderOverride = null) =>
            Path.Combine(startupFolderOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutName);

        /// <summary>True if both paths are the same file, ignoring case and relative parts.</summary>
        public static bool SamePath(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
