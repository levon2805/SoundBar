namespace SoundBar.Models
{
    /// <summary>
    /// A simple model to hold the details of an audio output device.
    /// Handy for showing the user what they're listening through.
    /// </summary>
    public class AudioDeviceModel
    {
        /// <summary>
        /// The unique system identifier for this specific device.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The friendly, human-readable name of the device (e.g., 'Headphones (Realtek)').
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Indicates if this is the device Windows is currently using by default.
        /// </summary>
        public bool IsDefault { get; set; }

        public override string ToString() => Name;
    }
}
