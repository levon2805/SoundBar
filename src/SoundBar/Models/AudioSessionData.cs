namespace SoundBar.Models
{
    /// <summary>
    /// A lightweight snapshot of an active audio session from the OS.
    /// This is used to pass data around without holding onto heavy COM objects.
    /// </summary>
    public readonly struct AudioSessionData
    {
        /// <summary>
        /// The process ID associated with this audio session.
        /// </summary>
        public int ProcessId { get; init; }

        /// <summary>
        /// The user-friendly name we'll show in the UI, usually cleaned up from the raw name.
        /// </summary>
        public string DisplayName { get; init; }

        /// <summary>
        /// The actual executable name (e.g., 'spotify.exe'). Essential for matching sessions.
        /// </summary>
        public string RawProcessName { get; init; }

        /// <summary>
        /// True if we suspect this app is running invisibly in the background without a main window.
        /// </summary>
        public bool IsBackgroundApp { get; init; }

        /// <summary>
        /// The current volume level of the session, from 0.0 to 1.0.
        /// </summary>
        public float Volume { get; init; }

        /// <summary>
        /// Indicates if the user has muted this specific session.
        /// </summary>
        public bool IsMuted { get; init; }

        /// <summary>
        /// Where we can find the executable to extract a nice icon from.
        /// </summary>
        public string? IconPath { get; init; }
    }
}
