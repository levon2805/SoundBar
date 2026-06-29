namespace SoundBar.Models
{
    public class AppSettings
    {
        // Default position and size if no config exists
        public double WindowTop { get; set; } = 100;
        public double WindowLeft { get; set; } = 100;
        public int WindowWidth { get; set; } = 400;
        public int WindowHeight { get; set; } = 500;
        public bool IsPinned { get; set; } = false;

        // List of app names that should be hidden from the main view
        public System.Collections.Generic.List<string> HiddenApps { get; set; } = new System.Collections.Generic.List<string>();

        // List of background app names that the user explicitly allows to be shown
        public System.Collections.Generic.List<string> AllowedBackgroundApps { get; set; } = new System.Collections.Generic.List<string>();

        // Saved audio presets
        public System.Collections.Generic.List<AudioPreset> Presets { get; set; } = new System.Collections.Generic.List<AudioPreset>();
    }

    public class AudioPreset
    {
        public string Name { get; set; } = string.Empty;
        public float MasterVolume { get; set; }
        // Key: Application executable name (e.g. "spotify.exe"), Value: Volume percentage 0-100 or float 0-1
        public System.Collections.Generic.Dictionary<string, float> AppVolumes { get; set; } = new System.Collections.Generic.Dictionary<string, float>();
    }
}