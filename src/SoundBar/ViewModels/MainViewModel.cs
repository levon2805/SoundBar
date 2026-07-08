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

        // Saved presets
        public ObservableCollection<AudioPreset> Presets { get; }
        public ObservableCollection<AudioDeviceModel> AudioDevices { get; }

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

        private AudioPreset? _selectedPreset;
        public AudioPreset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset != value)
                {
                    _selectedPreset = value;
                    OnPropertyChanged();
                    if (_selectedPreset != null)
                    {
                        ApplyPreset(_selectedPreset);
                    }
                }
            }
        }

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
                    UpdateStartupRegistry(value);
                }
            }
        }

        private void UpdateStartupRegistry(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                    {
                        string exePath = System.Environment.ProcessPath ?? "";
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue("SoundBar", $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue("SoundBar", false);
                    }
                }
            }
            catch { }
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
                    _masterVolumeDebounce?.Cancel();
                    // Don't Dispose the old CTS immediately — the in-flight Task.Delay may still
                    // reference its Token. Let the GC collect it after the task completes.
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
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

        // Constructor
        public MainViewModel(SettingsService settingsService)
        {
            _settingsService = settingsService;
            _updateService = new UpdateService();
            _mediaInfoService = new MediaInfoService();
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            // Connect to audio service
            _audioService = new WindowsAudioMixerService();

            // Setup data container
            Apps = new ObservableCollection<AudioAppModel>();
            HiddenApps = new ObservableCollection<string>(_settingsService.Settings.HiddenApps);
            SystemBackgroundApps = new ObservableCollection<string>();
            AllowedBackgroundApps = new ObservableCollection<string>(_settingsService.Settings.AllowedBackgroundApps);
            Presets = new ObservableCollection<AudioPreset>(_settingsService.Settings.Presets);
            AudioDevices = new ObservableCollection<AudioDeviceModel>();

            _progressTimer = new DispatcherTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
            _progressTimer.Tick += ProgressTimer_Tick;

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

            // Start the monitoring loop
            StartPolling();

            // Load Custom Background Image
            LoadBackgroundImageAsync();

            // Check for updates
            CheckForUpdatesAsync();
        }

        private async void CheckForUpdatesAsync()
        {
            bool hasUpdate = await _updateService.CheckForUpdatesAsync();
            if (hasUpdate)
            {
                _dispatcherQueue.TryEnqueue(() =>
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
                    _dispatcherQueue.TryEnqueue(() => BackgroundImage = null);
                    return; 
                }

                // Run disk I/O on a background thread
                string? imagePath = await System.Threading.Tasks.Task.Run(() =>
                {
                    var files = System.IO.Directory.GetFiles(folder);
                    return files.FirstOrDefault(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
                });

                if (imagePath != null)
                {
                    _dispatcherQueue.TryEnqueue(async () =>
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
                    _dispatcherQueue.TryEnqueue(() => BackgroundImage = null);
                }
            }
            catch
            {
                _dispatcherQueue.TryEnqueue(() => BackgroundImage = null);
            }
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

            // Create the thread
            var thread = new Thread(() =>
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

                        // Get Do Not Disturb status
                        bool systemDnd = _audioService.GetSystemSoundsMute();

                        // Update UI thread safely
                        _dispatcherQueue.TryEnqueue(() =>
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
                        });
                    }
                    catch (System.Exception)
                    {
                        // Ignore errors for now
                    }

                    // Poll every 1 second but respond to cancellation quickly
                    int slept = 0;
                    while (slept < 1000 && !token.IsCancellationRequested)
                    {
                        Thread.Sleep(100);
                        slept += 100;
                    }
                }
            });

            // Force thread to be MTA
            thread.SetApartmentState(ApartmentState.MTA);

            // Make this a background thread so it dies when the App closes
            thread.IsBackground = true;

            // Start the thread
            thread.Start();
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
                    _dispatcherQueue.TryEnqueue(() =>
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

        // Presets Logic
        public void SavePreset(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName)) return;

            var newPreset = new AudioPreset
            {
                Name = presetName,
                MasterVolume = MasterVolumePercentage / 100f
            };

            foreach (var app in Apps)
            {
                if (!string.IsNullOrEmpty(app.RawProcessName))
                {
                    newPreset.AppVolumes[app.RawProcessName] = app.Volume;
                }
            }

            // Remove if preset with same name exists
            var existing = Presets.FirstOrDefault(p => p.Name == presetName);
            if (existing != null)
            {
                Presets.Remove(existing);
            }

            Presets.Add(newPreset);
            _settingsService.Settings.Presets = Presets.ToList();
            _settingsService.SaveSettings();

            SelectedPreset = newPreset;
        }

        public void ApplyPreset(AudioPreset preset)
        {
            if (preset == null) return;

            // Apply Master Volume
            MasterVolumePercentage = (int)Math.Round(preset.MasterVolume * 100);

            // Apply App Volumes
            foreach (var app in Apps)
            {
                if (!string.IsNullOrEmpty(app.RawProcessName) && preset.AppVolumes.ContainsKey(app.RawProcessName))
                {
                    app.Volume = preset.AppVolumes[app.RawProcessName];
                    app.PushVolumeToOS(); // Force OS to sync immediately
                }
            }
        }

        public void DeletePreset(AudioPreset preset)
        {
            if (preset == null || !Presets.Contains(preset)) return;

            Presets.Remove(preset);
            _settingsService.Settings.Presets = Presets.ToList();
            _settingsService.SaveSettings();

            if (SelectedPreset == preset)
            {
                SelectedPreset = null;
            }
        }

        // --- Music Player Mode ---
        private bool _isMusicPlayerMode = false;
        public bool IsMusicPlayerMode
        {
            get => _isMusicPlayerMode;
            set
            {
                if (_isMusicPlayerMode != value)
                {
                    _isMusicPlayerMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MusicPlayerViewVisibility));
                    OnPropertyChanged(nameof(MixerViewVisibility));
                }
            }
        }

        public Microsoft.UI.Xaml.Visibility MusicPlayerViewVisibility => IsMusicPlayerMode ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        public Microsoft.UI.Xaml.Visibility MixerViewVisibility => IsMusicPlayerMode ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

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
        private DispatcherTimer _progressTimer;
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
            _dispatcherQueue.TryEnqueue(() =>
            {
                _basePosition = e.Position;
                _lastUpdatedTime = e.LastUpdatedTime;
                _isPlaying = e.IsPlaying;
                
                CurrentSongDurationSeconds = e.EndTime.TotalSeconds;
                
                if (!IsUserScrubbing)
                {
                    CurrentSongPositionSeconds = e.Position.TotalSeconds;
                }

                if (_isPlaying && !_progressTimer.IsEnabled)
                    _progressTimer.Start();
                else if (!_isPlaying && _progressTimer.IsEnabled)
                    _progressTimer.Stop();
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
            _dispatcherQueue.TryEnqueue(async () =>
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
                _isUpdatingDeviceFromSystem = false;
            }
        }

        public void CreateDesktopShortcut()
        {
            try
            {
                Type wshShellType = Type.GetTypeFromProgID("WScript.Shell");
                if (wshShellType != null)
                {
                    dynamic shell = Activator.CreateInstance(wshShellType);
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to create shortcut: " + ex.Message);
            }
        }

        public void Dispose()
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;

            _progressTimer?.Stop();

            _mediaInfoService?.Dispose();

            // Dispose all AudioAppModels to cancel any in-flight debounce tasks
            foreach (var app in Apps)
            {
                app.Dispose();
            }

            if (_audioService is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}