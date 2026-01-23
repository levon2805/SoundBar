using SoundBar.Models;
using SoundBar.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

            // Load the initial data
            LoadApps();
        }

        public void LoadApps()
        {
            // Create the thread
            var thread = new Thread(() =>
            {
                try
                {
                    // Get fresh list from the backend
                    var sessions = _audioService.GetActiveAudioSessions();

                    // Update UI thread safely
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Clear old list and add new items
                        Apps.Clear();
                        foreach (var app in sessions)
                        {
                            Apps.Add(app);
                        }
                    });
                }
                catch (System.Exception)
                {
                    // Ignore errors for now
                }
            });

            // Force thread to be MTA
            thread.SetApartmentState(ApartmentState.MTA);

            // Start the thread
            thread.Start();
        }

        // Standard for MVVM updates
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}