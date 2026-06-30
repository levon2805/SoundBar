namespace SoundBar.Models
{
    public class AudioDeviceModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }

        public override string ToString() => Name;
    }
}
