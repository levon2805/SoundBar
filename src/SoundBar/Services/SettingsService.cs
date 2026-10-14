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
    public class SettingsService : IDisposable
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
                    Normalise(settings);

                    _lastKnownJson = json;
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

                        // Remember what we wrote, so the file watcher knows this change was ours
                        _lastKnownJson = json;

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

        /// <summary>
        /// Someone might have hand-edited the config, so pull anything nonsensical back into line.
        /// </summary>
        internal static void Normalise(AppSettings settings)
        {
            // Make sure the window can't end up invisible
            settings.WindowOpacity = Math.Clamp(settings.WindowOpacity, AppSettings.MinWindowOpacity, 100);

            // A port has to be 1-65535, otherwise the server could never start
            if (settings.CompanionServerPort < 1 || settings.CompanionServerPort > 65535)
            {
                settings.CompanionServerPort = new AppSettings().CompanionServerPort;
            }

            settings.HiddenApps ??= new();
            settings.AllowedBackgroundApps ??= new();
            settings.AppAliases ??= new();
            settings.VolumeUpHotkey ??= string.Empty;
            settings.VolumeDownHotkey ??= string.Empty;
            settings.MuteHotkey ??= string.Empty;
            settings.InputMuteHotkey ??= string.Empty;
        }

        // --- Picking up edits made to config.json while SoundBar is running ---

        /// <summary>
        /// Fired (on a background thread) when someone edits and saves config.json while we're running.
        /// Carries the freshly loaded settings. Use <see cref="ReplaceSettings"/> to switch over to them.
        /// </summary>
        public event Action<AppSettings>? ChangedExternally;

        // The file's contents as we last read or wrote them. Anything else on disk came from someone else.
        // Guarded by _fileLock.
        private string? _lastKnownJson;

        private FileSystemWatcher? _watcher;
        private System.Threading.Timer? _externalChangeTimer;

        /// <summary>
        /// How long to wait after the file changes before reading it. Editors often save in a few steps.
        /// </summary>
        private const int ExternalChangeDelayMs = 300;

        /// <summary>
        /// Starts watching config.json, so edits made by hand apply straight away. Without this, our next
        /// save (closing the window saves its position, for one) would quietly overwrite them.
        /// </summary>
        public void StartWatchingForExternalChanges()
        {
            if (_watcher != null) return;

            try
            {
                string fullPath = Path.GetFullPath(_filePath);
                _externalChangeTimer = new System.Threading.Timer(_ => CheckForExternalChange(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

                _watcher = new FileSystemWatcher(Path.GetDirectoryName(fullPath)!, Path.GetFileName(fullPath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };
                _watcher.Changed += OnConfigFileEvent;
                _watcher.Created += OnConfigFileEvent;
                _watcher.Renamed += OnConfigFileEvent; // Many editors save to a temp file and rename it over the top
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't watch the config file: {ex.Message}");
                StopWatchingForExternalChanges();
            }
        }

        public void StopWatchingForExternalChanges()
        {
            _watcher?.Dispose();
            _watcher = null;
            _externalChangeTimer?.Dispose();
            _externalChangeTimer = null;
        }

        /// <summary>
        /// Switches over to settings that were edited by hand (see <see cref="ChangedExternally"/>).
        /// </summary>
        public void ReplaceSettings(AppSettings settings)
        {
            Settings = settings;
        }

        private void OnConfigFileEvent(object sender, FileSystemEventArgs e)
        {
            // Restart the countdown on every event, so we read the file once the editor's finished with it
            try { _externalChangeTimer?.Change(ExternalChangeDelayMs, System.Threading.Timeout.Infinite); } catch (ObjectDisposedException) { }
        }

        private void CheckForExternalChange()
        {
            string? json = null;

            // The editor might still have the file open, so give it a few goes
            for (int attempt = 0; attempt < 5 && json == null; attempt++)
            {
                try
                {
                    if (!File.Exists(_filePath)) return;
                    json = File.ReadAllText(_filePath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }

            if (json == null) return;

            lock (_fileLock)
            {
                // Our own save, or nothing actually changed
                if (json == _lastKnownJson) return;
            }

            AppSettings? fresh;
            try
            {
                fresh = JsonSerializer.Deserialize<AppSettings>(json);
            }
            catch (JsonException)
            {
                // Half-saved or a typo. Keep what we've got rather than wiping everything back to defaults.
                System.Diagnostics.Debug.WriteLine("Ignored an edit to config.json that isn't valid JSON.");
                return;
            }

            if (fresh == null) return;
            Normalise(fresh);

            lock (_fileLock)
            {
                _lastKnownJson = json;
            }

            ChangedExternally?.Invoke(fresh);
        }

        public void Dispose()
        {
            StopWatchingForExternalChanges();
        }
    }
}
