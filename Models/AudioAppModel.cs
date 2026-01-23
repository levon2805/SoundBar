using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SoundBar.Models
{
    // Interface that tells UI the value has changed and needs updating
    public class AudioAppModel : INotifyPropertyChanged
    {
        // Process ID
        public int ProcessId { get; set; }

        // The display name
        public string Name { get; set; }

        // Path to the .exe
        public string IconPath { get; set; }

        // The Volume level (0.0 to 1.0)
        private float _volume;

        public float Volume
        {
            get => _volume;
            set
            {
                // Only notify if the value actually changes to save performance
                if (_volume != value)
                {
                    _volume = value;
                    // This triggers the event that the UI listens for
                    OnPropertyChanged();
                }
            }
        }

        // Whether the app is muted
        private bool _isMuted;

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    OnPropertyChanged();
                }
            }
        }

        // Code required by INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}