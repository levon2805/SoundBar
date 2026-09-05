using SoundBar.Models;
using SoundBar.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using SoundBar.Helpers;
using Microsoft.UI.Xaml;

namespace SoundBar.ViewModels
{
    /// <summary>
    /// The grand orchestrator of the entire application.
    /// It brings together the audio services, media controls, and settings,
    /// serving everything up on a silver platter for the user interface.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IAudioMixerService _audioService;
        private readonly SettingsService _settingsService;
        private readonly UpdateService _updateService;
        private readonly MediaInfoService _mediaInfoService;
        private readonly HotkeyService _hotkeyService;
        private CompanionServerService? _companionServer;

        /// <summary>
        /// A list of application executable names that the user prefers to keep out of sight.
        /// </summary>
        public ObservableCollection<string> HiddenApps { get; set; }

        /// <summary>
        /// A list of background applications that the user explicitly wants to see in the mixer.
        /// </summary>
        public ObservableCollection<string> AllowedBackgroundApps { get; set; }

        /// <summary>
        /// A raw list of background apps detected by the system, just so the UI can show them in settings.
        /// </summary>
        public ObservableCollection<string> SystemBackgroundApps { get; set; }

        /// <summary>
        /// The main collection of active audio applications. When we add or remove items here, the UI updates automatically.
        /// </summary>
        public ObservableCollection<AudioAppModel> Apps { get; set; }

        
        /// <summary>
        /// The list of audio output devices currently detected by the system.
        /// </summary>
        public ObservableCollection<AudioDeviceModel> AudioDevices { get; private set; }

        private AudioDeviceModel? _selectedAudioDevice;
        public AudioDeviceModel? SelectedAudioDevice
        {
            get => _selectedAudioDevice;
            set
            {
                if (_selectedAudioDevice != value)
                {
                    _selectedAudioDevice = value;
                    OnPropertyChanged();

                    if (_selectedAudioDevice != null && !_isUpdatingDeviceFromSystem)
                    {
                        _audioService.SetDefaultAudioDevice(_selectedAudioDevice.Id);
                    }
                }
            }
        }
        private bool _isUpdatingDeviceFromSystem = false;



        public List<AppTheme> AvailableThemes { get; } = new List<AppTheme>
        {
            AppTheme.System,
            AppTheme.Light,
            AppTheme.Dark
        };

        public event EventHandler<AppTheme>? ThemeChanged;

        public AppTheme SelectedTheme
        {
            get => _settingsService.Settings.Theme;
            set
            {
                if (_settingsService.Settings.Theme != value)
                {
                    _settingsService.Settings.Theme = value;
                    _settingsService.SaveSettings();
                    OnPropertyChanged();
                    ThemeChanged?.Invoke(this, value);
                }
            }
        }

        // Update properties
        private bool _updateAvailable;
        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set
            {
                if (_updateAvailable != value)
                {
                    _updateAvailable = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UpdateBannerVisibility));
                }
            }
        }

        private string _latestVersion = string.Empty;
        public string LatestVersion
        {
            get => _latestVersion;
            set
            {
                if (_latestVersion != value)
                {
                    _latestVersion = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UpdateBannerText));
                }
            }
        }

        private bool _isUpdating;
        public bool IsUpdating
        {
            get => _isUpdating;
            set
            {
                if (_isUpdating != value)
                {
                    _isUpdating = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UpdateBannerText));
                }
            }
        }

        public Microsoft.UI.Xaml.Visibility UpdateBannerVisibility => UpdateAvailable ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public string UpdateBannerText => IsUpdating ? "Downloading Update..." : $"Update Available ({LatestVersion}) - Click to Install";

        public string AppVersionText => $"SoundBar {UpdateService.CurrentVersion}";

        // Background Image Property
        private Microsoft.UI.Xaml.Media.ImageSource? _backgroundImage;
        public Microsoft.UI.Xaml.Media.ImageSource? BackgroundImage
        {
            get => _backgroundImage;
            private set
            {
                if (_backgroundImage != value)
                {
                    _backgroundImage = value;
                    OnPropertyChanged();
                }
            }
        }

        // Do Not Disturb Property
        private bool _isDoNotDisturbEnabled;
        public bool IsDoNotDisturbEnabled
        {
            get => _isDoNotDisturbEnabled;
            set
            {
                if (_isDoNotDisturbEnabled != value)
                {
                    _isDoNotDisturbEnabled = value;
                    OnPropertyChanged();

                    if (!_isUpdatingDndFromSystem)
                    {
                        _audioService.SetSystemSoundsMute(_isDoNotDisturbEnabled);
                        
                        // Save setting
                        _settingsService.Settings.IsDoNotDisturbEnabled = _isDoNotDisturbEnabled;
                        _settingsService.SaveSettings();
                    }
                }
            }
        }
        private bool _isUpdatingDndFromSystem = false;

        // Loudness Warning Properties
        private bool _isLoudnessWarningEnabled;
        public bool IsLoudnessWarningEnabled
        {
            get => _isLoudnessWarningEnabled;
            set
            {
                if (_isLoudnessWarningEnabled != value)
                {
                    _isLoudnessWarningEnabled = value;
                    OnPropertyChanged();
                    _settingsService.Settings.IsLoudnessWarningEnabled = value;
                    _settingsService.SaveSettings();

                    if (!value)
                    {
                        ShowLoudnessWarning = false;
                        _highVolumeStartTime = null;
                    }
                }
            }
        }

        private bool _showLoudnessWarning;
        public bool ShowLoudnessWarning
        {
            get => _showLoudnessWarning;
            set
            {
                if (_showLoudnessWarning != value)
                {
                    _showLoudnessWarning = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LoudnessWarningVisibility));
                }
            }
        }

        public Microsoft.UI.Xaml.Visibility LoudnessWarningVisibility => ShowLoudnessWarning ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        private DateTime? _highVolumeStartTime = null;
        private bool _isLoudnessWarningDismissed = false;

        public void DismissLoudnessWarning()
        {
            ShowLoudnessWarning = false;
            _isLoudnessWarningDismissed = true;
        }

        public bool EnableFocusHighlight
        {
            get => _settingsService.Settings.EnableFocusHighlight;
            set
            {
                if (_settingsService.Settings.EnableFocusHighlight != value)
                {
                    _settingsService.Settings.EnableFocusHighlight = value;
                    OnPropertyChanged();
                    _settingsService.SaveSettings();
                }
            }
        }

        // Companion Server Properties
        public bool IsCompanionServerRunning => _companionServer?.IsRunning ?? false;

        public string CompanionServerUrl => _companionServer?.GetConnectionUrl() ?? $"http://localhost:{_settingsService.Settings.CompanionServerPort}";

        public string CompanionPairingCode => _companionServer?.PairingCode ?? "--";

        public int CompanionConnectedClients => _companionServer?.ConnectedClientCount ?? 0;
        public string CompanionClientText => CompanionConnectedClients > 0 ? $"{CompanionConnectedClients} client(s) connected" : "Waiting for connection...";

        public Uri? CompanionQrUrl
        {
            get
            {
                if (!IsCompanionServerRunning || string.IsNullOrEmpty(CompanionServerUrl)) return null;
                return new Uri($"https://api.qrserver.com/v1/create-qr-code/?size=180x180&data={Uri.EscapeDataString(CompanionServerUrl)}&bgcolor=1a1a1a&color=ffffff&margin=10");
            }
        }

        public bool EnableCompanionServer
        {
            get => _settingsService.Settings.EnableCompanionServer;
            set
            {
                if (_settingsService.Settings.EnableCompanionServer != value)
                {
                    _settingsService.Settings.EnableCompanionServer = value;
                    OnPropertyChanged();
                    _settingsService.SaveSettings();

                    if (value)
                        StartCompanionServer();
                    else
                        StopCompanionServer(userExplicit: true);
                }
            }
        }

        public void StartCompanionServer()
        {
            if (_companionServer != null && _companionServer.IsRunning) return;

            _companionServer = new CompanionServerService(
                _audioService,
                _mediaInfoService,
                () => Apps,
                () => AudioDevices,
                () => SelectedAudioDevice,
                (deviceId) =>
                {
                    RunOnUIThread(() =>
                    {
                        var device = AudioDevices.FirstOrDefault(d => d.Id == deviceId);
                        if (device != null)
                        {
                            SelectedAudioDevice = device;
                        }
                    });
                },
                () => InputDevices,
                () => SelectedInputDevice,
                (deviceId) =>
                {
                    RunOnUIThread(() =>
                    {
                        var device = InputDevices.FirstOrDefault(d => d.Id == deviceId);
                        if (device != null)
                        {
                            SelectedInputDevice = device;
                        }
                    });
                },
                _settingsService.Settings.CompanionServerPort
            );

            _companionServer.StateChanged += () =>
            {
                RunOnUIThread(() =>
                {
                    OnPropertyChanged(nameof(IsCompanionServerRunning));
                    OnPropertyChanged(nameof(CompanionServerUrl));
                    OnPropertyChanged(nameof(CompanionPairingCode));
                    OnPropertyChanged(nameof(CompanionConnectedClients));
                    OnPropertyChanged(nameof(CompanionClientText));
                    OnPropertyChanged(nameof(CompanionQrUrl));
                    OnPropertyChanged(nameof(CompanionPowerButtonVisibility));
                    OnPropertyChanged(nameof(CompanionActiveUiVisibility));
                });
            };

            _companionServer.Start();
            OnPropertyChanged(nameof(IsCompanionServerRunning));
            OnPropertyChanged(nameof(CompanionServerUrl));
            OnPropertyChanged(nameof(CompanionPairingCode));
            OnPropertyChanged(nameof(CompanionPowerButtonVisibility));
            OnPropertyChanged(nameof(CompanionActiveUiVisibility));
            OnPropertyChanged(nameof(CompanionClientText));
            OnPropertyChanged(nameof(CompanionQrUrl));
        }

        public void StopCompanionServer()
        {
            StopCompanionServer(userExplicit: false);
        }

        public void StopCompanionServer(bool userExplicit)
        {
            if (_companionServer != null)
            {
                _companionServer.Dispose();
                _companionServer = null;
            }

            // Only reset the saved preference if the user explicitly turned it off,
            // NOT if the server crashed or port was unavailable. This preserves their
            // preference so the server auto-starts again on next launch.
            if (userExplicit && _settingsService.Settings.EnableCompanionServer)
            {
                _settingsService.Settings.EnableCompanionServer = false;
                _settingsService.SaveSettings();
                OnPropertyChanged(nameof(EnableCompanionServer));
            }

            OnPropertyChanged(nameof(IsCompanionServerRunning));
            OnPropertyChanged(nameof(CompanionConnectedClients));
            OnPropertyChanged(nameof(CompanionPowerButtonVisibility));
            OnPropertyChanged(nameof(CompanionActiveUiVisibility));
            OnPropertyChanged(nameof(CompanionClientText));
            OnPropertyChanged(nameof(CompanionQrUrl));
        }

        // Timestamp for Master Volume
        private DateTime _lastMasterVolumeChange = DateTime.MinValue;
        private System.Threading.CancellationTokenSource? _masterVolumeDebounce;

        private bool _showMediaControls;
        public bool ShowMediaControls
        {
            get => _showMediaControls;
            set
            {
                if (_showMediaControls != value)
                {
                    _showMediaControls = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MediaControlsVisibility));
                    _settingsService.Settings.ShowMediaControls = value;
                    _settingsService.SaveSettings();
                }
            }
        }

        public Microsoft.UI.Xaml.Visibility MediaControlsVisibility => ShowMediaControls ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        // --- Input Device (Microphone) Properties ---

        /// <summary>
        /// The list of audio input devices (like microphones) currently detected by the system.
        /// </summary>
        public ObservableCollection<AudioDeviceModel> InputDevices { get; } = new();

        private AudioDeviceModel? _selectedInputDevice;
        public AudioDeviceModel? SelectedInputDevice
        {
            get => _selectedInputDevice;
            set
            {
                if (_selectedInputDevice != value)
                {
                    _selectedInputDevice = value;
                    OnPropertyChanged();

                    if (_selectedInputDevice != null && !_isUpdatingInputDeviceFromSystem)
                    {
                        _audioService.SetDefaultInputDevice(_selectedInputDevice.Id);
                    }
                }
            }
        }
        private bool _isUpdatingInputDeviceFromSystem = false;

        private bool _isInputMuted;
        public bool IsInputMuted
        {
            get => _isInputMuted;
            set
            {
                if (_isInputMuted != value)
                {
                    _isInputMuted = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(InputMuteIcon));
                }
            }
        }

        /// <summary>
        /// Returns the appropriate mic glyph: muted mic (red) or live mic.
        /// The colour is handled in XAML binding.
        /// </summary>
        public string InputMuteIcon => IsInputMuted ? "\uF12E" : "\uE720"; // F12E is MicOff (slash), E720 is live Mic

        public void ToggleInputMute()
        {
            bool newState = !_isInputMuted;
            _audioService.SetInputMute(newState);
            IsInputMuted = newState;
        }

        private void UpdateInputDevices(List<AudioDeviceModel> inputDevices)
        {
            // Only rebuild if the device list actually changed
            var currentIds = InputDevices.Select(d => d.Id).ToList();
            var newIds = inputDevices.Select(d => d.Id).ToList();

            if (!currentIds.SequenceEqual(newIds))
            {
                _isUpdatingInputDeviceFromSystem = true;
                InputDevices.Clear();
                foreach (var device in inputDevices)
                {
                    InputDevices.Add(device);
                }
                _isUpdatingInputDeviceFromSystem = false;
            }

            // Sync the selected device
            var defaultDevice = inputDevices.FirstOrDefault(d => d.IsDefault);
            if (defaultDevice != null && (_selectedInputDevice == null || _selectedInputDevice.Id != defaultDevice.Id))
            {
                _isUpdatingInputDeviceFromSystem = true;
                SelectedInputDevice = InputDevices.FirstOrDefault(d => d.Id == defaultDevice.Id);
                _isUpdatingInputDeviceFromSystem = false;
            }
            else if (inputDevices.Count == 0 && _selectedInputDevice != null)
            {
                // No devices available — clear selection so placeholder text is shown
                _isUpdatingInputDeviceFromSystem = true;
                SelectedInputDevice = null;
                _isUpdatingInputDeviceFromSystem = false;
            }
        }

        // --- Layout Visibility Properties ---

        public bool ShowOutputDevice
        {
            get => _settingsService.Settings.ShowOutputDevice;
            set
            {
                if (_settingsService.Settings.ShowOutputDevice != value)
                {
                    _settingsService.Settings.ShowOutputDevice = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(OutputDeviceVisibility));
                    _settingsService.SaveSettings();
                }
            }
        }
        public Visibility OutputDeviceVisibility => ShowOutputDevice ? Visibility.Visible : Visibility.Collapsed;

        public bool ShowInputDevice
        {
            get => _settingsService.Settings.ShowInputDevice;
            set
            {
                if (_settingsService.Settings.ShowInputDevice != value)
                {
                    _settingsService.Settings.ShowInputDevice = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(InputDeviceVisibility));
                    _settingsService.SaveSettings();
                }
            }
        }
        public Visibility InputDeviceVisibility => ShowInputDevice ? Visibility.Visible : Visibility.Collapsed;

        public bool ShowMasterVolume
        {
            get => _settingsService.Settings.ShowMasterVolume;
            set
            {
                if (_settingsService.Settings.ShowMasterVolume != value)
                {
                    _settingsService.Settings.ShowMasterVolume = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MasterVolumeVisibility));
                    _settingsService.SaveSettings();
                }
            }
        }
        public Visibility MasterVolumeVisibility => ShowMasterVolume ? Visibility.Visible : Visibility.Collapsed;

        public bool ShowActiveApps
        {
            get => _settingsService.Settings.ShowActiveApps;
            set
            {
                if (_settingsService.Settings.ShowActiveApps != value)
                {
                    _settingsService.Settings.ShowActiveApps = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActiveAppsVisibility));
                    _settingsService.SaveSettings();
                }
            }
        }
        public Visibility ActiveAppsVisibility => ShowActiveApps ? Visibility.Visible : Visibility.Collapsed;

        // Run At Startup Property
        private bool _runAtStartup;
        public bool RunAtStartup
        {
            get => _runAtStartup;
            set
            {
                if (_runAtStartup != value)
                {
                    _runAtStartup = value;
                    OnPropertyChanged();
                    _settingsService.Settings.RunAtStartup = value;
                    _settingsService.SaveSettings();
                    UpdateStartupShortcut(value);
                }
            }
        }

        private void UpdateStartupShortcut(bool enable)
        {
            try
            {
                // Clean up the old registry key if it exists
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    if (key != null && key.GetValue("SoundBar") != null)
                    {
                        key.DeleteValue("SoundBar", false);
                    }
                }
                catch { }

                string startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = System.IO.Path.Combine(startupPath, "SoundBar.lnk");

                if (enable)
                {
                    Type? wshShellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (wshShellType != null)
                    {
                        dynamic? shell = Activator.CreateInstance(wshShellType);
                        if (shell != null)
                        {
                            dynamic shortcut = shell.CreateShortcut(shortcutPath);
                            shortcut.TargetPath = System.Environment.ProcessPath;
                            shortcut.WorkingDirectory = AppContext.BaseDirectory;
                            shortcut.Save();
                        }
                    }
                }
                else
                {
                    if (System.IO.File.Exists(shortcutPath))
                    {
                        System.IO.File.Delete(shortcutPath);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update startup shortcut: {ex.Message}");
            }
        }

        // Master Volume Property
        private float _masterVolume;
        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                if (_masterVolume != value)
                {
                    _masterVolume = value;

                    // Update timestamp on user interaction
                    _lastMasterVolumeChange = DateTime.Now;

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MasterVolumePercentage));

                    // Send command to Windows (DEBOUNCED to prevent COM/GC pressure when dragging)
                    var oldCts = _masterVolumeDebounce;
                    oldCts?.Cancel();
                    _masterVolumeDebounce = new System.Threading.CancellationTokenSource();
                    var token = _masterVolumeDebounce.Token;
                    var capturedVolume = _masterVolume;

                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            await System.Threading.Tasks.Task.Delay(50, token);
                            if (!token.IsCancellationRequested)
                            {
                                _audioService.SetMasterVolume(capturedVolume);
                            }
                        }
                        catch (System.Threading.Tasks.TaskCanceledException) { }
                        finally
                        {
                            // Now safe to dispose — the Task.Delay has completed or been cancelled
                            oldCts?.Dispose();
                        }
                    });
                }
            }
        }

        // The master volume represented as a percentage (0 to 100)
        public int MasterVolumePercentage
        {
            get => (int)Math.Round(_masterVolume * 100);
            set
            {
                MasterVolume = value / 100f;
            }
        }

        // Dispatcher to safely update UI from background threads
        private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

        // Constructor for DI / Testing
        internal MainViewModel(SettingsService settingsService, IAudioMixerService audioService, UpdateService updateService = null, MediaInfoService mediaInfoService = null, HotkeyService hotkeyService = null)
        {
            _settingsService = settingsService;
            _updateService = updateService ?? new UpdateService();
            _mediaInfoService = mediaInfoService ?? new MediaInfoService();
            _hotkeyService = hotkeyService ?? new HotkeyService();
            _hotkeyService.KeyPressed += HotkeyService_KeyPressed;
            try { _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); } catch { _dispatcherQueue = null; }
            _audioService = audioService;

            InitializeInternal();
        }

        private void RunOnUIThread(Microsoft.UI.Dispatching.DispatcherQueueHandler action)
        {
            if (_dispatcherQueue != null)
                _dispatcherQueue.TryEnqueue(action);
            else
                action();
        }

        // Default Constructor
        public MainViewModel(SettingsService settingsService) : this(settingsService, new WindowsAudioMixerService())
        {
        }

        private void InitializeInternal()
        {
            // Setup data container
            Apps = new ObservableCollection<AudioAppModel>();
            HiddenApps = new ObservableCollection<string>(_settingsService.Settings.HiddenApps);
            SystemBackgroundApps = new ObservableCollection<string>();
            AllowedBackgroundApps = new ObservableCollection<string>(_settingsService.Settings.AllowedBackgroundApps);
            AudioDevices = new ObservableCollection<AudioDeviceModel>();

            try 
            {
                _progressTimer = new DispatcherTimer();
                _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
                _progressTimer.Tick += ProgressTimer_Tick;
            }
            catch { }

            _mediaInfoService.MediaInfoChanged += MediaInfoService_MediaInfoChanged;
            _mediaInfoService.TimelineInfoChanged += MediaInfoService_TimelineInfoChanged;
            _ = _mediaInfoService.InitializeAsync();

            // Initial load of master volume
            _masterVolume = _audioService.GetMasterVolume();

            // Initial load of settings
            _isDoNotDisturbEnabled = _settingsService.Settings.IsDoNotDisturbEnabled;
            _isLoudnessWarningEnabled = _settingsService.Settings.IsLoudnessWarningEnabled;
            _showMediaControls = _settingsService.Settings.ShowMediaControls;
            _runAtStartup = _settingsService.Settings.RunAtStartup;
            _audioService.SetSystemSoundsMute(_isDoNotDisturbEnabled);

            // Ensure startup state is correctly applied (also cleans up old registry keys)
            UpdateStartupShortcut(_runAtStartup);

            // Start the monitoring loop
            StartPolling();

            // Load Custom Background Image
            LoadBackgroundImageAsync();

            // Check for updates
            CheckForUpdatesAsync();
        }

        private void HotkeyService_KeyPressed(object? sender, HotkeyEventArgs e)
        {
            RunOnUIThread(() =>
            {
                string pressedString = ParseHotkeyToString(e.Key, e.Modifiers);

                if (IsRecordingHotkey)
                {
                    RecordedHotkeyString = pressedString;
                    e.Handled = true;
                    return;
                }

                if (pressedString == InputMuteHotkey)
                {
                    ToggleInputMute();
                    e.Handled = true;
                    return;
                }

                var activeApps = Apps.Where(a => a.IsFocused).ToList();
                if (!activeApps.Any()) return;

                foreach (var activeApp in activeApps)
                {
                    if (pressedString == VolumeUpHotkey)
                    {
                        activeApp.VolumePercentage = Math.Min(100, activeApp.VolumePercentage + 5);
                        e.Handled = true;
                    }
                    else if (pressedString == VolumeDownHotkey)
                    {
                        activeApp.VolumePercentage = Math.Max(0, activeApp.VolumePercentage - 5);
                        e.Handled = true;
                    }
                    else if (pressedString == MuteHotkey)
                    {
                        activeApp.IsMuted = !activeApp.IsMuted;
                        e.Handled = true;
                    }
                }
            });
        }

        private string ParseHotkeyToString(Windows.System.VirtualKey key, HotkeyModifiers modifiers)
        {
            string modStr = "";
            if (modifiers.HasFlag(HotkeyModifiers.Control)) modStr += "Control+";
            if (modifiers.HasFlag(HotkeyModifiers.Alt)) modStr += "Alt+";
            if (modifiers.HasFlag(HotkeyModifiers.Shift)) modStr += "Shift+";

            return modStr + key.ToString();
        }

        public string VolumeUpHotkey
        {
            get => _settingsService.Settings.VolumeUpHotkey;
            set
            {
                if (_settingsService.Settings.VolumeUpHotkey != value)
                {
                    _settingsService.Settings.VolumeUpHotkey = value;
                    _settingsService.SaveSettings();
                    OnPropertyChanged();
                }
            }
        }

        public string VolumeDownHotkey
        {
            get => _settingsService.Settings.VolumeDownHotkey;
            set
            {
                if (_settingsService.Settings.VolumeDownHotkey != value)
                {
                    _settingsService.Settings.VolumeDownHotkey = value;
                    _settingsService.SaveSettings();
                    OnPropertyChanged();
                }
            }
        }

        public string MuteHotkey
        {
            get => _settingsService.Settings.MuteHotkey;
            set
            {
                if (_settingsService.Settings.MuteHotkey != value)
                {
                    _settingsService.Settings.MuteHotkey = value;
                    _settingsService.SaveSettings();
                    OnPropertyChanged();
                }
            }
        }

        public string InputMuteHotkey
        {
            get => _settingsService.Settings.InputMuteHotkey;
            set
            {
                if (_settingsService.Settings.InputMuteHotkey != value)
                {
                    _settingsService.Settings.InputMuteHotkey = value;
                    _settingsService.SaveSettings();
                    OnPropertyChanged();
                }
            }
        }

        private bool _isRecordingHotkey;
        public bool IsRecordingHotkey
        {
            get => _isRecordingHotkey;
            set
            {
                if (_isRecordingHotkey != value)
                {
                    _isRecordingHotkey = value;
                    OnPropertyChanged();
                    if (value) RecordedHotkeyString = "Listening...";
                }
            }
        }

        private string _recordedHotkeyString = "Listening...";
        public string RecordedHotkeyString
        {
            get => _recordedHotkeyString;
            set
            {
                if (_recordedHotkeyString != value)
                {
                    _recordedHotkeyString = value;
                    OnPropertyChanged();
                }
            }
        }

        private async void CheckForUpdatesAsync()
        {
            bool hasUpdate = await _updateService.CheckForUpdatesAsync();
            if (hasUpdate)
            {
                RunOnUIThread(() =>
                {
                    LatestVersion = _updateService.LatestVersion;
                    UpdateAvailable = true;
                });
            }
        }

        public async void ApplyUpdate()
        {
            if (IsUpdating) return;
            
            IsUpdating = true;
            await _updateService.DownloadAndApplyUpdateAsync();
            IsUpdating = false;
        }

        public async void LoadBackgroundImageAsync()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = System.IO.Path.Combine(appData, "SoundBar", "Backgrounds");

                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                    RunOnUIThread(() => BackgroundImage = null);
                    return; 
                }

                // Run disk I/O on a background thread
                string? imagePath = await System.Threading.Tasks.Task.Run(() =>
                {
                    var files = System.IO.Directory.GetFiles(folder);
                    return files.FirstOrDefault(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));
                });

                if (imagePath != null)
                {
                    RunOnUIThread(async () =>
                    {
                        try
                        {
                            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                            
                            // MEMORY OPTIMISATION: Constrain the decoded image size.
                            // A raw 4K wallpaper consumes ~33MB of RAM. Limiting it to 800px width 
                            // keeps memory usage tiny while looking crystal clear on the widget.
                            bitmap.DecodePixelWidth = 800;

                            // Use FileShare.ReadWrite so we don't crash if the user is mid-copying a file
                            using var stream = System.IO.File.Open(imagePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                            using var randomAccessStream = stream.AsRandomAccessStream();
                            await bitmap.SetSourceAsync(randomAccessStream);
                            BackgroundImage = bitmap;
                        }
                        catch
                        {
                            // If the file is heavily locked or corrupted, silently ignore
                            // rather than clearing their existing background.
                        }
                    });
                }
                else
                {
                    RunOnUIThread(() => BackgroundImage = null);
                }
            }
            catch
            {
                RunOnUIThread(() => BackgroundImage = null);
            }
        }

        public void ForceRefreshAudioSessions()
        {
            _audioService.ClearCache();
        }

        public void OpenBackgroundFolder()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = System.IO.Path.Combine(appData, "SoundBar", "Backgrounds");

                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = folder,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch { }
        }

        public void ReloadBackground()
        {
            LoadBackgroundImageAsync();
        }



        private System.Threading.CancellationTokenSource? _pollingCts;

        public void StartPolling()
        {
            _pollingCts = new System.Threading.CancellationTokenSource();
            var token = _pollingCts.Token;

            // Use the .NET Thread Pool instead of dedicating a raw OS thread
            _ = Task.Run(async () =>
            {
                // Loop forever to keep checking for changes
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Get lightweight session snapshots from the backend (zero side effects)
                        var sessions = _audioService.GetActiveAudioSessions();

                        // Get System Volume
                        var systemVol = _audioService.GetMasterVolume();

                        // Get Audio Devices
                        var audioDevices = _audioService.GetAudioDevices();

                        // Get Input Devices and Mic Mute status
                        var inputDevices = _audioService.GetInputDevices();
                        bool inputMuted = _audioService.GetInputMute();

                        // Get Do Not Disturb status
                        bool systemDnd = _audioService.GetSystemSoundsMute();

                        // Resolve foreground process name on the background thread (avoids UI-thread Process allocations)
                        uint activePid = Helpers.WindowHelper.GetForegroundProcessId();
                        string activeProcessName = "";
                        try
                        {
                            if (activePid > 0)
                            {
                                using var proc = System.Diagnostics.Process.GetProcessById((int)activePid);
                                activeProcessName = proc.ProcessName;
                            }
                        }
                        catch { }

                        // Update UI thread safely
                        RunOnUIThread(() =>
                        {
                            UpdateCollection(sessions);

                            // Only update Master Volume if user hasn't touched it for 2 seconds
                            if ((DateTime.Now - _lastMasterVolumeChange).TotalSeconds > 2)
                            {
                                // Sync Master Volume (Update backing field to avoid triggering setter loop)
                                if (_masterVolume != systemVol)
                                {
                                    _masterVolume = systemVol;
                                    OnPropertyChanged(nameof(MasterVolume));
                                    OnPropertyChanged(nameof(MasterVolumePercentage));
                                }
                            }

                            // Loudness Warning Logic
                            if (_isLoudnessWarningEnabled)
                            {
                                if (_masterVolume > 0.85f)
                                {
                                    if (_highVolumeStartTime == null)
                                    {
                                        _highVolumeStartTime = DateTime.Now;
                                        _isLoudnessWarningDismissed = false;
                                    }
                                    else if (!_isLoudnessWarningDismissed && !_showLoudnessWarning)
                                    {
                                        // 1 hour (3600 seconds)
                                        if ((DateTime.Now - _highVolumeStartTime.Value).TotalSeconds >= 3600)
                                        {
                                            ShowLoudnessWarning = true;
                                        }
                                    }
                                }
                                else
                                {
                                    _highVolumeStartTime = null;
                                    ShowLoudnessWarning = false;
                                    _isLoudnessWarningDismissed = false;
                                }
                            }

                            // Sync Do Not Disturb status from system
                            if (_isDoNotDisturbEnabled != systemDnd)
                            {
                                _isUpdatingDndFromSystem = true;
                                IsDoNotDisturbEnabled = systemDnd;
                                _isUpdatingDndFromSystem = false;
                            }

                            // Sync Audio Devices
                            UpdateAudioDevices(audioDevices);

                            // Sync Input Devices
                            UpdateInputDevices(inputDevices);

                            // Sync Mic Mute status from system
                            if (_isInputMuted != inputMuted)
                            {
                                IsInputMuted = inputMuted;
                            }

                            // Update Focus Outline (support multi-process apps like Discord)
                            foreach (var app in Apps)
                            {
                                bool isMatch = false;
                                if (activePid > 0)
                                {
                                    if (app.ProcessId == activePid)
                                    {
                                        isMatch = true;
                                    }
                                    else if (!string.IsNullOrEmpty(activeProcessName) && !string.IsNullOrEmpty(app.RawProcessName))
                                    {
                                        string rawNoExt = app.RawProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                                            ? app.RawProcessName.Substring(0, app.RawProcessName.Length - 4) 
                                            : app.RawProcessName;
                                            
                                        if (rawNoExt.Equals(activeProcessName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            isMatch = true;
                                        }
                                    }
                                }
                                app.IsFocused = isMatch;
                            }
                        });
                    }
                    catch (System.Exception)
                    {
                        // Ignore errors for now
                    }

                    try
                    {
                        await Task.Delay(1000, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            });
        }

        private void HandleAliasChanged(string rawProcessName, string newAlias)
        {
            if (string.IsNullOrEmpty(rawProcessName)) return;

            if (string.IsNullOrWhiteSpace(newAlias))
            {
                // Revert to original display name if cleared
                _settingsService.Settings.AppAliases.Remove(rawProcessName);
                
                var app = Apps.FirstOrDefault(a => string.Equals(a.RawProcessName, rawProcessName, StringComparison.OrdinalIgnoreCase));
                if (app != null)
                {
                    // Execute on the next UI tick so the TextBox has finished its TwoWay update
                    RunOnUIThread(() =>
                    {
                        app.AliasChanged -= HandleAliasChanged; // Temporarily unsubscribe to prevent loop
                        app.Name = app.DisplayName;
                        app.AliasChanged += HandleAliasChanged;
                    });
                }
            }
            else
            {
                _settingsService.Settings.AppAliases[rawProcessName] = newAlias;
            }
            _settingsService.SaveSettings();
        }

        private void UpdateCollection(List<AudioSessionData> latestSessions)
        {
            // Remove apps that are no longer running
            // Loop backwards so we can remove items safely
            for (int i = Apps.Count - 1; i >= 0; i--)
            {
                var existingApp = Apps[i];

                // If existing app is not in the new list (match by RawProcessName since DisplayName can change and Name is now an alias)
                if (!latestSessions.Any(x => string.Equals(x.RawProcessName, existingApp.RawProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    bool isProcessDead = true;
                        // FAST PATH: Check if the specific process ID we started with is still running
                        try
                        {
                            using var p = System.Diagnostics.Process.GetProcessById(existingApp.ProcessId);
                            // If we can get it, it hasn't exited, AND the process name matches (prevents Windows PID recycling bug)
                            if (!p.HasExited && string.Equals(p.ProcessName, existingApp.RawProcessName, StringComparison.OrdinalIgnoreCase))
                            {
                                isProcessDead = false;
                            }
                        }
                        catch (ArgumentException)
                        {
                            // Process is definitely dead
                            isProcessDead = true;
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            // We don't have access to check HasExited, but the process exists
                            isProcessDead = false;
                        }
                        catch (InvalidOperationException)
                        {
                            // Process exited during the check
                            isProcessDead = true;
                        }
                        catch
                        {
                            // Any other error, assume dead
                            isProcessDead = true;
                        }

                    if (isProcessDead)
                    {
                        Apps.RemoveAt(i);
                    }
                    else
                    {
                        existingApp.IsSessionAlive = false;
                    }
                }
            }

            // Add new apps or update existing ones
            foreach (var sessionData in latestSessions)
            {
                if (string.IsNullOrEmpty(sessionData.DisplayName)) continue;

                if (sessionData.IsBackgroundApp)
                {
                    // If it's a background app but not allowed, add to system list and skip
                    if (!AllowedBackgroundApps.Any(a => string.Equals(a, sessionData.DisplayName, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!SystemBackgroundApps.Any(a => string.Equals(a, sessionData.DisplayName, StringComparison.OrdinalIgnoreCase)))
                        {
                            SystemBackgroundApps.Add(sessionData.DisplayName);
                        }
                        
                        // Remove from active Apps if it was previously there
                        var existingBg = Apps.FirstOrDefault(a => string.Equals(a.RawProcessName, sessionData.RawProcessName, StringComparison.OrdinalIgnoreCase));
                        if (existingBg != null) Apps.Remove(existingBg);
                        
                        continue;
                    }
                }

                // Skip if this app is hidden by the user
                if (HiddenApps.Any(a => string.Equals(a, sessionData.DisplayName, StringComparison.OrdinalIgnoreCase)))
                {
                    var existingHidden = Apps.FirstOrDefault(a => string.Equals(a.RawProcessName, sessionData.RawProcessName, StringComparison.OrdinalIgnoreCase));
                    if (existingHidden != null) Apps.Remove(existingHidden);
                    continue;
                }

                // If this app is not in our current UI list, create a new AudioAppModel for it
                if (!Apps.Any(x => string.Equals(x.RawProcessName, sessionData.RawProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    // Check if there is a saved alias
                    string aliasName = _settingsService.Settings.AppAliases.TryGetValue(sessionData.RawProcessName ?? "", out var alias) ? alias : sessionData.DisplayName;

                    // ONLY place where AudioAppModel is created — for genuinely new apps
                    var newApp = new AudioAppModel(_audioService)
                    {
                        ProcessId = sessionData.ProcessId,
                        IsBackgroundApp = sessionData.IsBackgroundApp,
                        DisplayName = sessionData.DisplayName,
                        RawProcessName = sessionData.RawProcessName,
                        IconPath = sessionData.IconPath
                    };
                    
                    // Set Name BEFORE assigning AliasChanged so the initial set
                    // doesn't trigger a save to config.json for every app
                    newApp.Name = aliasName;
                    newApp.AliasChanged = HandleAliasChanged;

                    // Set volume/mute via backing fields to avoid triggering OS write-back
                    newApp.SyncVolumeFromOS(sessionData.Volume);
                    newApp.SyncMuteFromOS(sessionData.IsMuted);

                    Apps.Add(newApp);
                    _ = newApp.LoadIconAsync(); // Fire and forget
                }
                else
                {
                    // Update existing app (match by RawProcessName)
                    var existingApp = Apps.First(x => string.Equals(x.RawProcessName, sessionData.RawProcessName, StringComparison.OrdinalIgnoreCase));

                    // Check if the session just came back to life after being destroyed (e.g. tabbing back into a game)
                    if (!existingApp.IsSessionAlive)
                    {
                        existingApp.IsSessionAlive = true;
                        existingApp.ProcessId = sessionData.ProcessId;
                        existingApp.RawProcessName = sessionData.RawProcessName;
                        
                        // Push our cached UI volume down to the new audio session
                        existingApp.PushVolumeToOS();
                    }
                    else if ((DateTime.Now - existingApp.LastModified).TotalSeconds > 2)
                    {
                        // Sync the volume from the OS (only if user hasn't recently moved the slider)
                        // Use SyncFromOS methods to avoid writing back to the OS
                        existingApp.SyncVolumeFromOS(sessionData.Volume);
                        existingApp.SyncMuteFromOS(sessionData.IsMuted);
                    }
                }
            }
        }

        // Hides an app from the main view
        public void HideApp(string appName)
        {
            if (string.IsNullOrEmpty(appName) || HiddenApps.Any(a => string.Equals(a, appName, StringComparison.OrdinalIgnoreCase))) return;

            HiddenApps.Add(appName);
            _settingsService.Settings.HiddenApps = HiddenApps.ToList();

            // Ensure mutual exclusivity: remove from AllowedBackgroundApps if present
            var existingBg = AllowedBackgroundApps.FirstOrDefault(a => string.Equals(a, appName, StringComparison.OrdinalIgnoreCase));
            if (existingBg != null)
            {
                AllowedBackgroundApps.Remove(existingBg);
                _settingsService.Settings.AllowedBackgroundApps = AllowedBackgroundApps.ToList();
            }

            _settingsService.SaveSettings();

            // Check if it's currently in the Apps list and remove it
            var appToRemove = Apps.FirstOrDefault(a => string.Equals(a.DisplayName, appName, StringComparison.OrdinalIgnoreCase));
            if (appToRemove != null)
            {
                Apps.Remove(appToRemove);
            }
        }

        // Unhides an app
        public void UnhideApp(string appName)
        {
            var existing = HiddenApps.FirstOrDefault(a => string.Equals(a, appName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                HiddenApps.Remove(existing);
                _settingsService.Settings.HiddenApps = HiddenApps.ToList();
                _settingsService.SaveSettings();
            }
        }

        // Allows a system background app to be shown
        public void AllowBackgroundApp(string appName)
        {
            if (string.IsNullOrEmpty(appName) || AllowedBackgroundApps.Any(a => string.Equals(a, appName, StringComparison.OrdinalIgnoreCase))) return;

            AllowedBackgroundApps.Add(appName);
            _settingsService.Settings.AllowedBackgroundApps = AllowedBackgroundApps.ToList();

            // Ensure mutual exclusivity: remove from HiddenApps if present
            var existingHidden = HiddenApps.FirstOrDefault(a => string.Equals(a, appName, StringComparison.OrdinalIgnoreCase));
            if (existingHidden != null)
            {
                HiddenApps.Remove(existingHidden);
                _settingsService.Settings.HiddenApps = HiddenApps.ToList();
            }

            _settingsService.SaveSettings();

            var existing = SystemBackgroundApps.FirstOrDefault(a => string.Equals(a, appName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                SystemBackgroundApps.Remove(existing);
            }
        }

        /// <summary>
        /// Opens the Windows System Sounds control panel so the user
        /// can manage sound schemes and programme event sounds.
        /// </summary>
        public void OpenSystemSounds()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = "shell32.dll,Control_RunDLL mmsys.cpl,,2",
                    UseShellExecute = false
                });
            }
            catch { }
        }

        /// <summary>
        /// Whether the user has completed the guided feature tour.
        /// </summary>
        public bool HasCompletedTour
        {
            get => _settingsService.Settings.HasCompletedTour;
            set
            {
                _settingsService.Settings.HasCompletedTour = value;
                _settingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to show the Feature Tour button in settings.
        /// </summary>
        public bool ShowFeatureTour
        {
            get => _settingsService.Settings.ShowFeatureTour;
            set
            {
                _settingsService.Settings.ShowFeatureTour = value;
                _settingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        // --- View Modes ---
        private bool _isMusicPlayerMode = false;
        public bool IsMusicPlayerMode
        {
            get => _isMusicPlayerMode;
            set
            {
                if (_isMusicPlayerMode != value)
                {
                    _isMusicPlayerMode = value;
                    if (value) IsCompanionViewMode = false; // Turn off companion view if turning on music player
                    
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MusicPlayerViewVisibility));
                    OnPropertyChanged(nameof(MixerViewVisibility));
                }
            }
        }

        private bool _isCompanionViewMode = false;
        public bool IsCompanionViewMode
        {
            get => _isCompanionViewMode;
            set
            {
                if (_isCompanionViewMode != value)
                {
                    _isCompanionViewMode = value;
                    if (value) IsMusicPlayerMode = false; // Turn off music player if turning on companion view
                    
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CompanionViewVisibility));
                    OnPropertyChanged(nameof(MixerViewVisibility));
                }
            }
        }

        public Microsoft.UI.Xaml.Visibility MusicPlayerViewVisibility => IsMusicPlayerMode ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility CompanionViewVisibility => IsCompanionViewMode ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility MixerViewVisibility => (IsMusicPlayerMode || IsCompanionViewMode) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        
        // Companion Server UI states (Power button vs QR code)
        public Microsoft.UI.Xaml.Visibility CompanionPowerButtonVisibility => IsCompanionServerRunning ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        public Microsoft.UI.Xaml.Visibility CompanionActiveUiVisibility => IsCompanionServerRunning ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        private string _currentSongTitle = "Not Playing";
        public string CurrentSongTitle
        {
            get => _currentSongTitle;
            set
            {
                if (_currentSongTitle != value)
                {
                    _currentSongTitle = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _currentSongArtist = string.Empty;
        public string CurrentSongArtist
        {
            get => _currentSongArtist;
            set
            {
                if (_currentSongArtist != value)
                {
                    _currentSongArtist = value;
                    OnPropertyChanged();
                }
            }
        }

        private Microsoft.UI.Xaml.Media.Imaging.BitmapImage? _currentSongThumbnail;
        public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? CurrentSongThumbnail
        {
            get => _currentSongThumbnail;
            set
            {
                if (_currentSongThumbnail != value)
                {
                    _currentSongThumbnail = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FallbackIconVisibility));
                }
            }
        }

        public Microsoft.UI.Xaml.Visibility FallbackIconVisibility => CurrentSongThumbnail == null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        // --- Timeline & Scrubbing ---
        private DispatcherTimer? _progressTimer;
        private TimeSpan _basePosition;
        private DateTimeOffset _lastUpdatedTime;
        private bool _isPlaying;
        public bool IsUserScrubbing { get; set; }

        private double _currentSongPositionSeconds;
        public double CurrentSongPositionSeconds
        {
            get => _currentSongPositionSeconds;
            set
            {
                if (_currentSongPositionSeconds != value)
                {
                    _currentSongPositionSeconds = value;
                    OnPropertyChanged();
                    CurrentSongPositionText = TimeSpan.FromSeconds(value).ToString(@"m\:ss");
                }
            }
        }

        private double _currentSongDurationSeconds = 1;
        public double CurrentSongDurationSeconds
        {
            get => _currentSongDurationSeconds;
            set
            {
                if (_currentSongDurationSeconds != value)
                {
                    _currentSongDurationSeconds = value;
                    OnPropertyChanged();
                    CurrentSongDurationText = TimeSpan.FromSeconds(value).ToString(@"m\:ss");
                }
            }
        }

        private string _currentSongPositionText = "0:00";
        public string CurrentSongPositionText
        {
            get => _currentSongPositionText;
            set
            {
                if (_currentSongPositionText != value)
                {
                    _currentSongPositionText = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _currentSongDurationText = "0:00";
        public string CurrentSongDurationText
        {
            get => _currentSongDurationText;
            set
            {
                if (_currentSongDurationText != value)
                {
                    _currentSongDurationText = value;
                    OnPropertyChanged();
                }
            }
        }

        private void MediaInfoService_TimelineInfoChanged(object? sender, TimelineInfoEventArgs e)
        {
            RunOnUIThread(() =>
            {
                _basePosition = e.Position;
                _lastUpdatedTime = e.LastUpdatedTime;
                _isPlaying = e.IsPlaying;
                
                CurrentSongDurationSeconds = e.EndTime.TotalSeconds;
                
                if (!IsUserScrubbing)
                {
                    CurrentSongPositionSeconds = e.Position.TotalSeconds;
                }

                if (_isPlaying && (_progressTimer?.IsEnabled == false))
                    _progressTimer?.Start();
                else if (!_isPlaying && (_progressTimer?.IsEnabled == true))
                    _progressTimer?.Stop();
            });
        }

        private void ProgressTimer_Tick(object? sender, object e)
        {
            if (!IsUserScrubbing && _isPlaying)
            {
                var timeSinceUpdate = DateTimeOffset.Now - _lastUpdatedTime;
                var currentPosition = _basePosition + timeSinceUpdate;
                if (currentPosition.TotalSeconds <= CurrentSongDurationSeconds)
                {
                    CurrentSongPositionSeconds = currentPosition.TotalSeconds;
                }
            }
        }

        public void SeekToScrubPosition()
        {
            var seekPosition = TimeSpan.FromSeconds(CurrentSongPositionSeconds);
            // Update base position immediately so ProgressTimer_Tick doesn't
            // snap the slider back to the old position before the OS reports the new one
            _basePosition = seekPosition;
            _lastUpdatedTime = DateTimeOffset.Now;
            _ = _mediaInfoService.SeekAsync(seekPosition);
        }

        private void MediaInfoService_MediaInfoChanged(object? sender, MediaInfoEventArgs e)
        {
            RunOnUIThread(async () =>
            {
                CurrentSongTitle = string.IsNullOrEmpty(e.Title) ? "Not Playing" : e.Title;
                CurrentSongArtist = e.Artist;

                if (e.Thumbnail != null)
                {
                    try
                    {
                        var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                        using var stream = await e.Thumbnail.OpenReadAsync();
                        await bmp.SetSourceAsync(stream);
                        CurrentSongThumbnail = bmp;
                    }
                    catch
                    {
                        CurrentSongThumbnail = null;
                    }
                }
                else
                {
                    CurrentSongThumbnail = null;
                }
            });
        }

        // Standard for MVVM updates
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void UpdateAudioDevices(List<AudioDeviceModel> latestDevices)
        {
            // Simple sync: just rebuild if counts differ or default changed, to avoid heavy UI churn.
            // A more complex sync could match IDs.
            bool needsRebuild = false;

            if (AudioDevices.Count != latestDevices.Count)
            {
                needsRebuild = true;
            }
            else
            {
                for (int i = 0; i < latestDevices.Count; i++)
                {
                    if (AudioDevices[i].Id != latestDevices[i].Id || AudioDevices[i].IsDefault != latestDevices[i].IsDefault)
                    {
                        needsRebuild = true;
                        break;
                    }
                }
            }

            if (needsRebuild)
            {
                _isUpdatingDeviceFromSystem = true;
                AudioDevices.Clear();
                AudioDeviceModel? newDefault = null;

                foreach (var device in latestDevices)
                {
                    AudioDevices.Add(device);
                    if (device.IsDefault)
                    {
                        newDefault = device;
                    }
                }

                if (newDefault != null && (SelectedAudioDevice == null || SelectedAudioDevice.Id != newDefault.Id))
                {
                    SelectedAudioDevice = newDefault;
                }
                else if (latestDevices.Count == 0 && SelectedAudioDevice != null)
                {
                    // No devices available — clear selection so placeholder text is shown
                    SelectedAudioDevice = null;
                }
                _isUpdatingDeviceFromSystem = false;
            }
        }

        public void CreateDesktopShortcut()
        {
            try
            {
                Type? wshShellType = Type.GetTypeFromProgID("WScript.Shell");
                if (wshShellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(wshShellType);
                    if (shell != null)
                    {
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        string shortcutPath = System.IO.Path.Combine(desktopPath, "SoundBar.lnk");
                        
                        dynamic shortcut = shell.CreateShortcut(shortcutPath);
                        shortcut.TargetPath = Environment.ProcessPath;
                    shortcut.WorkingDirectory = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
                        shortcut.Description = "SoundBar Audio Mixer";
                        shortcut.IconLocation = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SoundBar.ico");
                        shortcut.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to create shortcut: " + ex.Message);
            }
        }

        public void OpenSettingsFile()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = System.IO.Path.Combine(appData, "SoundBar");
                string filePath = System.IO.Path.Combine(folder, "config.json");
                
                if (System.IO.File.Exists(filePath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open config file: {ex.Message}");
            }
        }

        public void OpenReleaseNotes()
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"https://github.com/levon2805/SoundBar/releases/tag/{UpdateService.CurrentVersion}",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open release notes: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _companionServer?.Dispose();

            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;

            _masterVolumeDebounce?.Cancel();
            _masterVolumeDebounce?.Dispose();
            _masterVolumeDebounce = null;

            _progressTimer?.Stop();

            _mediaInfoService?.Dispose();
            _hotkeyService?.Dispose();

            // Dispose all AudioAppModels to cancel any in-flight debounce tasks
            foreach (var app in Apps)
            {
                app.Dispose();
            }

            if (_audioService is IDisposable disposable)
            {
                disposable.Dispose();
            }

            if (_updateService is IDisposable updateDisposable)
            {
                updateDisposable.Dispose();
            }
        }
    }
}