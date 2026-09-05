namespace SoundBar.Models
{
    /// <summary>
    /// Represents the core settings for the application. 
    /// Everything here is saved so you don't lose your setup when you close the app.
    /// </summary>
    public class AppSettings
    {
        // We set some sensible defaults just in case there isn't a config file yet.
        public double WindowTop { get; set; } = 100;
        public double WindowLeft { get; set; } = 100;
        public int WindowWidth { get; set; } = 400;
        public int WindowHeight { get; set; } = 500;
        
        /// <summary>
        /// Keeps the window pinned always on top of other windows.
        /// </summary>
        public bool IsPinned { get; set; } = false;

        /// <summary>
        /// Mutes system sounds when enabled, brilliant for avoiding interruptions.
        /// </summary>
        public bool IsDoNotDisturbEnabled { get; set; } = false;

        /// <summary>
        /// Shows a warning if the master volume gets worryingly loud.
        /// </summary>
        public bool IsLoudnessWarningEnabled { get; set; } = true;

        /// <summary>
        /// Whether the media playback controls should be visible.
        /// </summary>
        public bool ShowMediaControls { get; set; } = true;

        /// <summary>
        /// Whether the application should run automatically when Windows starts.
        /// </summary>
        public bool RunAtStartup { get; set; } = false;

        /// <summary>
        /// A list of application names that the user prefers to keep hidden from the main view.
        /// </summary>
        public System.Collections.Generic.List<string> HiddenApps { get; set; } = new System.Collections.Generic.List<string>();

        /// <summary>
        /// A list of background applications that the user explicitly wants to see in the mixer.
        /// </summary>
        public System.Collections.Generic.List<string> AllowedBackgroundApps { get; set; } = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Whether the user has completed the guided feature tour.
        /// </summary>
        public bool HasCompletedTour { get; set; } = false;

        /// <summary>
        /// Whether to show the Feature Tour button in settings.
        /// </summary>
        public bool ShowFeatureTour { get; set; } = true;

        /// <summary>
        /// Any custom nicknames the user has given to their apps (e.g. renaming 'chrome' to 'Browser').
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string> AppAliases { get; set; } = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>
        /// The visual theme of the application (Light, Dark, or System).
        /// </summary>
        public AppTheme Theme { get; set; } = AppTheme.System;

        /// <summary>
        /// Whether to show a visual highlight outline around the currently focused application.
        /// </summary>
        public bool EnableFocusHighlight { get; set; } = true;

        /// <summary>
        /// Hotkey configuration string for increasing the focused app's volume. Format: "Ctrl+Alt+Up"
        /// </summary>
        public string VolumeUpHotkey { get; set; } = "Control+Alt+Up";

        /// <summary>
        /// Hotkey configuration string for decreasing the focused app's volume. Format: "Ctrl+Alt+Down"
        /// </summary>
        public string VolumeDownHotkey { get; set; } = "Control+Alt+Down";

        /// <summary>
        /// Hotkey configuration string for muting/unmuting the focused app. Format: "Ctrl+Alt+M"
        /// </summary>
        public string MuteHotkey { get; set; } = "Control+Alt+M";

        /// <summary>
        /// Hotkey configuration string for muting/unmuting the microphone. Format: "Ctrl+Alt+I"
        /// </summary>
        public string InputMuteHotkey { get; set; } = "Control+Alt+I";

        /// <summary>
        /// Whether the companion web server should be enabled for remote control from a phone.
        /// </summary>
        public bool EnableCompanionServer { get; set; } = false;

        /// <summary>
        /// The port number for the companion web server.
        /// </summary>
        public int CompanionServerPort { get; set; } = 6767;

        // --- Layout Visibility Settings ---
        // These let the user customise which sections appear on the main page.

        /// <summary>
        /// Whether the output device picker is visible on the main page.
        /// </summary>
        public bool ShowOutputDevice { get; set; } = true;

        /// <summary>
        /// Whether the input device (microphone) controls are visible on the main page.
        /// </summary>
        public bool ShowInputDevice { get; set; } = true;

        /// <summary>
        /// Whether the master volume slider is visible on the main page.
        /// </summary>
        public bool ShowMasterVolume { get; set; } = true;

        /// <summary>
        /// Whether the active apps list is visible on the main page.
        /// </summary>
        public bool ShowActiveApps { get; set; } = true;
    }

    /// <summary>
    /// Represents the different visual themes available in the app.
    /// </summary>
    public enum AppTheme
    {
        System,
        Light,
        Dark
    }

}