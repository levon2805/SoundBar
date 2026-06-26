using SoundBar.Models;
using SoundBar.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;


namespace SoundBar.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IAudioMixerService _audioService;
        private readonly SettingsService _settingsService;

        // List of hidden apps
        public ObservableCollection<string> HiddenApps { get; set; }

        // List of allowed background apps
        public ObservableCollection<string> AllowedBackgroundApps { get; set; }

        // List of raw system background apps for the UI to display
        public ObservableCollection<string> SystemBackgroundApps { get; set; }

        // Specia list, add/remove items here the UI auto updates itself
        public ObservableCollection<AudioAppModel> Apps { get; set; }

        // Timestamp for Master Volume
        private DateTime _lastMasterVolumeChange = DateTime.MinValue;

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

                    // Send command to Windows
                    _audioService.SetMasterVolume(_masterVolume);
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
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            // Setup data container
            Apps = new ObservableCollection<AudioAppModel>();

            // Connect to audio service
            _audioService = new WindowsAudioMixerService();

            // Load hidden apps from settings
            var settings = _settingsService.Load();
            HiddenApps = new ObservableCollection<string>(settings.HiddenApps ?? new List<string>());
            AllowedBackgroundApps = new ObservableCollection<string>(settings.AllowedBackgroundApps ?? new List<string>());
            SystemBackgroundApps = new ObservableCollection<string>();

            // Initial load of master volume
            _masterVolume = _audioService.GetMasterVolume();

            // Start the monitoring loop
            StartPolling();
        }

        public void StartPolling()
        {
            // Create the thread
            var thread = new Thread(() =>
            {
                // Loop forever to keep checking for changes
                while (true)
                {
                    try
                    {
                        // Get fresh list from the backend
                        var sessions = _audioService.GetActiveAudioSessions();

                        // Get System Volume
                        var systemVol = _audioService.GetMasterVolume();

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
                        });
                    }
                    catch (System.Exception)
                    {
                        // Ignore errors for now
                    }

                    // Poll every 1 second
                    Thread.Sleep(1000);
                }
            });

            // Force thread to be MTA
            thread.SetApartmentState(ApartmentState.MTA);

            // Make this a background thread so it dies when the App closes
            thread.IsBackground = true;

            // Start the thread
            thread.Start();
        }

        private void UpdateCollection(List<AudioAppModel> latestSessions)
        {
            // Remove apps that are no longer running
            // Loop backwards so we can remove items safely
            for (int i = Apps.Count - 1; i >= 0; i--)
            {
                var existingApp = Apps[i];

                // If existing app is not in the new list
                if (!latestSessions.Any(x => x.ProcessId == existingApp.ProcessId))
                {
                    bool isProcessDead = true;
                    try
                    {
                        var proc = System.Diagnostics.Process.GetProcessById(existingApp.ProcessId);
                        if (proc != null && !proc.HasExited)
                        {
                            // The game/app is still running, it just temporarily destroyed its audio session (e.g. tabbed out of a fullscreen game)
                            isProcessDead = false;
                        }
                    }
                    catch
                    {
                        // Process doesn't exist or we don't have access (it died)
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

            // Add new apps that just started
            foreach (var newApp in latestSessions)
            {
                if (string.IsNullOrEmpty(newApp.Name)) continue;

                if (newApp.IsBackgroundApp)
                {
                    // If it's a background app but not allowed, add to system list and skip
                    if (!AllowedBackgroundApps.Contains(newApp.Name))
                    {
                        if (!SystemBackgroundApps.Contains(newApp.Name))
                        {
                            SystemBackgroundApps.Add(newApp.Name);
                        }
                        continue;
                    }
                }

                // Skip if this app is hidden by the user
                if (HiddenApps.Contains(newApp.Name))
                {
                    continue;
                }

                // If new app is not in our current list
                if (!Apps.Any(x => x.ProcessId == newApp.ProcessId))
                {
                    Apps.Add(newApp);
                }
                else
                {
                    // Update existing app
                    var existingApp = Apps.First(x => x.ProcessId == newApp.ProcessId);

                    // Check if the session just came back to life after being destroyed (e.g. tabbing back into a game)
                    if (!existingApp.IsSessionAlive)
                    {
                        existingApp.IsSessionAlive = true;
                        
                        // Push our cached UI volume down to the new audio session
                        existingApp.PushVolumeToOS();
                    }
                    else if ((DateTime.Now - existingApp.LastModified).TotalSeconds > 2)
                    {
                        // Sync the volume from the OS (only if user hasn't recently moved the slider)
                        if (existingApp.Volume != newApp.Volume)
                        {
                            existingApp.Volume = newApp.Volume;
                        }

                        if (existingApp.IsMuted != newApp.IsMuted)
                        {
                            existingApp.IsMuted = newApp.IsMuted;
                        }
                    }
                }
            }
        }

        // Hides an app from the main view
        public void HideApp(string appName)
        {
            if (string.IsNullOrEmpty(appName) || HiddenApps.Contains(appName)) return;

            HiddenApps.Add(appName);
            SaveHiddenApps();

            // Immediately remove it from the active UI list
            var appToRemove = Apps.FirstOrDefault(a => a.Name == appName);
            if (appToRemove != null)
            {
                Apps.Remove(appToRemove);
            }
        }

        // Unhides an app so it can be seen again
        public void UnhideApp(string appName)
        {
            if (string.IsNullOrEmpty(appName) || !HiddenApps.Contains(appName)) return;

            HiddenApps.Remove(appName);
            SaveHiddenApps();

            // The background poller will automatically pick it back up on the next tick
        }

        // Saves the current HiddenApps list to settings
        private void SaveHiddenApps()
        {
            var settings = _settingsService.Load();
            settings.HiddenApps = HiddenApps.ToList();
            _settingsService.Save(settings);
        }

        // Allows a background app to be shown
        public void AllowBackgroundApp(string appName)
        {
            if (string.IsNullOrEmpty(appName) || AllowedBackgroundApps.Contains(appName)) return;

            AllowedBackgroundApps.Add(appName);
            SystemBackgroundApps.Remove(appName);

            var settings = _settingsService.Load();
            settings.AllowedBackgroundApps = AllowedBackgroundApps.ToList();
            _settingsService.Save(settings);
        }

        // Standard for MVVM updates
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}