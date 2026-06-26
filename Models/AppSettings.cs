namespace SoundBar.Models
{
    public class AppSettings
    {
        // Default position if no config exists
        public double WindowTop { get; set; } = 100;
        public double WindowLeft { get; set; } = 100;
        public bool IsPinned { get; set; } = false;

        // List of app names that should be hidden from the main view
        public System.Collections.Generic.List<string> HiddenApps { get; set; } = new System.Collections.Generic.List<string>();
    }
}