using SoundBar.Models;
using SoundBar.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
            // Get fresh list from the backend
            var sessions = _audioService.GetActiveAudioSessions();

            // Clear old list and add new items
            Apps.Clear();
            foreach (var app in sessions)
            {
                Apps.Add(app);
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
