namespace SoundBar.Models
{
    /// <summary>
    /// Lightweight data-only snapshot of an audio session.
    /// Used by the polling loop to carry volume/mute info without triggering
    /// any OS side effects (unlike AudioAppModel whose property setters call SetVolume/SetMute).
    /// </summary>
    public readonly struct AudioSessionData
    {
        public int ProcessId { get; init; }
        public string DisplayName { get; init; }
        public string RawProcessName { get; init; }
        public bool IsBackgroundApp { get; init; }
        public float Volume { get; init; }
        public bool IsMuted { get; init; }
        public string? IconPath { get; init; }
    }
}
