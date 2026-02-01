namespace SoundBar.Models
{
    public class AppSettings
    {
        // Default position if no config exists
        public double WindowTop { get; set; } = 100;
        public double WindowLeft { get; set; } = 100;
        public bool IsPinned { get; set; } = false;
    }
}