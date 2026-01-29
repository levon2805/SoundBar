using SoundBar.Models;
using SoundBar.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;

namespace SoundBar.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IAudioMixerService _audioService;

        // Specia list, add/remove items here the UI auto updates itself
        public ObservableCollection<AudioAppModel> Apps { get; set; }

        public MainViewModel()
        {
            // Setup data container
            Apps = new ObservableCollection<AudioAppModel>();

            // Connect to audio service
            _audioService = new WindowsAudioMixerService();

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

                        // Update UI thread safely
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            UpdateCollection(sessions);
                        });
                    }
                    catch (System.Exception)
                    {
                        // Ignore errors for now
                    }

                    // Wait 3 seconds before checking again
                    Thread.Sleep(3000);
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
                    Apps.RemoveAt(i);
                }
            }

            // Add new apps that just started
            foreach (var newApp in latestSessions)
            {
                // If new app is not in our current list
                if (!Apps.Any(x => x.ProcessId == newApp.ProcessId))
                {
                    Apps.Add(newApp);
                }
            }
        }

        // Standard for MVVM updates
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}