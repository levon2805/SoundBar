using System;
using System.Runtime.InteropServices;

namespace SoundBar.Shared
{
    /// <summary>
    /// Reads and writes Windows shortcut (.lnk) files using Windows' own shell object. Every shell object is let
    /// go of straight away rather than whenever the garbage collector gets round to it.
    /// </summary>
    internal static class ShortcutFile
    {
        /// <summary>The program a shortcut opens, or null if it can't be read.</summary>
        public static string? ReadTarget(string shortcutPath)
        {
            object? shell = null;
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return null;
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return null;

                dynamic shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
                try
                {
                    return (string?)shortcut.TargetPath;
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }

        /// <summary>Makes (or replaces) a shortcut to a program.</summary>
        public static void Save(string shortcutPath, string targetPath, string? workingDirectory, string? description = null)
        {
            object? shell = null;
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return;

                dynamic shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
                try
                {
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = workingDirectory;
                    if (description != null) shortcut.Description = description;
                    shortcut.Save();
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
