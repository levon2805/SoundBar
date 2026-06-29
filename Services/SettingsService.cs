using SoundBar.Models;
using System;
using System.IO;
using System.Text.Json;

namespace SoundBar.Services
{
    public class SettingsService
    {
        private readonly string _filePath;

        public AppSettings Settings { get; private set; }

        public SettingsService()
        {
            // Save 'config.json' in %APPDATA%\SoundBar
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "SoundBar");
            
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            _filePath = Path.Combine(folder, "config.json");
            Settings = Load();
        }

        public void SaveSettings()
        {
            Save(Settings);
        }

        public AppSettings Load()
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
                // If file is corrupt, return defaults
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            try
            {
                // Format the JSON so it's readable if you open the file manually
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }
}