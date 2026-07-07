using SoundBar.Models;
using System;
using System.IO;
using System.Text.Json;

namespace SoundBar.Services
{
    /// <summary>
    /// Handles all the reading and writing of our config file.
    /// Ensures your precious settings are safe and sound between sessions.
    /// </summary>
    public class SettingsService
    {
        private readonly string _filePath;
        private readonly object _fileLock = new object();

        /// <summary>
        /// The currently loaded settings, ready to be used.
        /// </summary>
        public AppSettings Settings { get; private set; }

        /// <summary>
        /// Sets up the service and figures out where to stick the config file.
        /// </summary>
        public SettingsService()
        {
            // We pop the config.json neatly into the user's AppData roaming folder.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "SoundBar");
            
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            _filePath = Path.Combine(folder, "config.json");
            Settings = Load();
        }

        /// <summary>
        /// Internal constructor used exclusively for testing so we don't clobber real settings.
        /// </summary>
        internal SettingsService(string customFilePath)
        {
            _filePath = customFilePath;
            Settings = Load();
        }

        /// <summary>
        /// A handy wrapper to save the current settings back to disk.
        /// </summary>
        public void SaveSettings()
        {
            Save(Settings);
        }

        /// <summary>
        /// Reads the config file from disk. If things go pear-shaped, we just return the defaults.
        /// </summary>
        public AppSettings Load()
        {
            lock (_fileLock)
            {
                if (!File.Exists(_filePath))
                    return new AppSettings();

                try
                {
                    string json = File.ReadAllText(_filePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch
                {
                    // Oh dear, the file is corrupt. Let's just return a fresh slate rather than crashing.
                    return new AppSettings();
                }
            }
        }

        /// <summary>
        /// Writes the settings to disk, nicely formatted so you can peek at it in Notepad.
        /// </summary>
        public void Save(AppSettings settings)
        {
            lock (_fileLock)
            {
                try
                {
                    // WriteIndented makes the JSON much nicer for humans to read.
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(settings, options);
                    File.WriteAllText(_filePath, json);
                }
                catch
                {
                    // We just ignore save errors to prevent interrupting the user.
                    // It's not ideal, but better than a hard crash.
                }
            }
        }
    }
}