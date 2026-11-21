using System.Text.Json;
using SoundBar.Shared;

namespace SoundBarTaskbar
{
    /// <summary>
    /// The helper's own small settings file (taskbar.json, next to SoundBar's config.json). Only the helper
    /// writes it, so it can never clash with SoundBar saving its settings.
    /// </summary>
    internal sealed class HelperSettings
    {
        /// <summary>The monitor the indicator was moved to (for example \\.\DISPLAY2), or null for the main one.</summary>
        public string? Monitor { get; set; }

        public static HelperSettings Load(string? path = null)
        {
            path ??= TaskbarHelperInfo.SettingsFilePath;
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<HelperSettings>(File.ReadAllText(path)) ?? new HelperSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't read taskbar.json: {ex.Message}");
            }
            return new HelperSettings();
        }

        public void Save(string? path = null)
        {
            path ??= TaskbarHelperInfo.SettingsFilePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Write to a spare file first, so a crash mid-write can't leave a broken file behind
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(this));
                File.Move(temp, path, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't save taskbar.json: {ex.Message}");
            }
        }
    }
}
