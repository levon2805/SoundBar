using System.Collections.Generic;

namespace SoundBar.Services
{
    /// <summary>
    /// The complete state snapshot broadcast to all connected companion clients every 500ms.
    /// This is the single source of truth for the PWA's entire UI.
    /// </summary>
    public class CompanionStateSnapshot
    {
        /// <summary>
        /// Master volume as a percentage (0–100).
        /// </summary>
        public int MasterVolume { get; set; }

        /// <summary>
        /// Whether the entire system is muted.
        /// </summary>
        public bool MasterMuted { get; set; }

        /// <summary>
        /// All active audio applications with their current volumes.
        /// </summary>
        public List<CompanionAppState> Apps { get; set; } = new();

        /// <summary>
        /// Currently playing media info (may be empty if nothing is playing).
        /// </summary>
        public CompanionNowPlaying? NowPlaying { get; set; }

        /// <summary>
        /// All available audio output devices.
        /// </summary>
        public List<CompanionAudioDevice> Devices { get; set; } = new();

        /// <summary>
        /// The ID of the currently selected output device.
        /// </summary>
        public string? SelectedDeviceId { get; set; }
    }

    /// <summary>
    /// Per-application audio state sent to the companion.
    /// </summary>
    public class CompanionAppState
    {
        /// <summary>
        /// The display name of the application (e.g. "Discord", "Spotify").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The raw process name used to target volume/mute commands.
        /// </summary>
        public string RawProcessName { get; set; } = string.Empty;

        /// <summary>
        /// Volume as a percentage (0–100).
        /// </summary>
        public int Volume { get; set; }

        /// <summary>
        /// Whether this application is currently muted.
        /// </summary>
        public bool IsMuted { get; set; }

        /// <summary>
        /// URL to the app's executable icon in PNG format, or null.
        /// </summary>
        public string? IconUrl { get; set; }
    }

    /// <summary>
    /// Media playback information for the "Now Playing" card.
    /// </summary>
    public class CompanionNowPlaying
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;

        /// <summary>
        /// URL to the JPEG album art, or null if unavailable.
        /// </summary>
        public string? AlbumArtUrl { get; set; }

        /// <summary>
        /// Current playback position in seconds.
        /// </summary>
        public double PositionSeconds { get; set; }

        /// <summary>
        /// Total track duration in seconds.
        /// </summary>
        public double DurationSeconds { get; set; }

        /// <summary>
        /// Whether media is currently playing (vs paused/stopped).
        /// </summary>
        public bool IsPlaying { get; set; }
    }

    /// <summary>
    /// Represents an audio output device for the companion's device switcher.
    /// </summary>
    public class CompanionAudioDevice
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Incoming command from the companion PWA.
    /// The "Action" field determines what happens, and the additional fields
    /// provide the parameters for that action.
    /// </summary>
    public class CompanionCommand
    {
        /// <summary>
        /// The action to perform. Supported values:
        /// "setAppVolume", "setAppMute", "setMasterVolume", "setMasterMute",
        /// "mediaPlayPause", "mediaNext", "mediaPrevious", "mediaSeek",
        /// "setOutputDevice", "pair"
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// The raw process name of the target application (for app-specific commands).
        /// </summary>
        public string? App { get; set; }

        /// <summary>
        /// The numeric value (volume percentage, seek position in seconds, etc.).
        /// </summary>
        public double? Value { get; set; }

        /// <summary>
        /// Boolean value (for mute toggles).
        /// </summary>
        public bool? BoolValue { get; set; }

        /// <summary>
        /// The device ID for output device switching.
        /// </summary>
        public string? DeviceId { get; set; }

        /// <summary>
        /// The pairing code entered by the user on the companion app.
        /// </summary>
        public string? PairingCode { get; set; }
    }
}
