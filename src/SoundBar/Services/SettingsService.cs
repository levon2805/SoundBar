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

        private AppSettings _settings = new AppSettings();
        
        /// <summary>
        /// The currently loaded settings, ready to be used.
        /// </summary>
        public AppSettings Settings
        {
            get { lock (_fileLock) { return _settings; } }
            private set { lock (_fileLock) { _settings = value; } }
        }

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
                    var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                    // Guard against hand-edited values that would make the window invisible
                    settings.WindowOpacity = Math.Clamp(settings.WindowOpacity, AppSettings.MinWindowOpacity, 100);

                    return settings;
                }
                catch (JsonException) 
                { 
                    try { File.Copy(_filePath, _filePath + ".corrupt", true); } catch { }
                    return new AppSettings(); 
                }
                catch (IOException) { return Settings ?? new AppSettings(); }
            }
        }

        /// <summary>
        /// Writes the settings to disk, nicely formatted so you can peek at it in Notepad.
        /// </summary>
        public void Save(AppSettings settings)
        {
            lock (_fileLock)
            {
                // Settings lists can be changed on another thread while we're serialising them,
                // which throws "Collection was modified". That's a momentary clash, so retry a few times.
                const int maxAttempts = 3;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        string json = JsonSerializer.Serialize(settings, _jsonOptions);
                        string tempPath = _filePath + ".tmp";
                        File.WriteAllText(tempPath, json);
                        File.Move(tempPath, _filePath, overwrite: true);
                        LastSaveError = null;
                        return;
                    }
                    catch (Exception ex) when (attempt < maxAttempts && (ex is InvalidOperationException || ex is IOException))
                    {
                        System.Threading.Thread.Sleep(20);
                    }
                    catch (Exception ex)
                    {
                        LastSaveError = ex.Message;
                        System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
                        return;
                    }
                }
            }
        }

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        /// <summary>
        /// The reason the last save failed, or null if it saved fine. Handy for diagnosing lost settings.
        /// </summary>
        public string? LastSaveError { get; private set; }
    }
}
