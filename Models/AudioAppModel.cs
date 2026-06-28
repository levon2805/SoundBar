using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using SoundBar.Services;

namespace SoundBar.Models
{
    // Interface that tells UI the value has changed and needs updating
    public class AudioAppModel : INotifyPropertyChanged
    {
        // Field definition
        private readonly IAudioMixerService _audioService;

        // Process ID
        public int ProcessId { get; set; }

        // The display name
        public string? Name { get; set; }

        // Path to the .exe
        public string? IconPath { get; set; }

        // The loaded icon for the UI
        private Microsoft.UI.Xaml.Media.ImageSource? _appIcon;
        public Microsoft.UI.Xaml.Media.ImageSource? AppIcon
        {
            get => _appIcon;
            private set
            {
                if (_appIcon != value)
                {
                    _appIcon = value;
                    OnPropertyChanged();
                }
            }
        }

        // Track when we last touched this slider
        public DateTime LastModified { get; private set; } = DateTime.MinValue;

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

                    // Update timestamp so we know the user is interacting
                    LastModified = DateTime.Now;

                    // This triggers the event that the UI listens for
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VolumePercentage));

                    // Actually changes the volume
                    if (_audioService != null && !string.IsNullOrEmpty(Name))
                    {
                        _audioService.SetVolume(Name, _volume);
                    }
                }
            }
        }

        /// <summary>
        /// Updates the volume from an OS read without writing it back (avoids feedback loop).
        /// Use this when syncing the slider to match what Windows reports.
        /// </summary>
        public void SyncVolumeFromOS(float volume)
        {
            if (_volume != volume)
            {
                _volume = volume;
                OnPropertyChanged(nameof(Volume));
                OnPropertyChanged(nameof(VolumePercentage));
            }
        }

        /// <summary>
        /// Updates the mute state from an OS read without writing it back (avoids feedback loop).
        /// </summary>
        public void SyncMuteFromOS(bool isMuted)
        {
            if (_isMuted != isMuted)
            {
                _isMuted = isMuted;
                OnPropertyChanged(nameof(IsMuted));
            }
        }

        // Forces the current UI volume down to the Windows Audio Service
        // Useful if the game destroyed its audio session and just recreated a new one
        public void PushVolumeToOS()
        {
            if (_audioService != null && !string.IsNullOrEmpty(Name))
            {
                _audioService.SetVolume(Name, _volume);
            }
        }

        // The volume represented as a percentage (0 to 100)
        public int VolumePercentage
        {
            get => (int)Math.Round(_volume * 100);
            set
            {
                Volume = value / 100f;
            }
        }

        // True if the app does not have a visible main window
        public bool IsBackgroundApp { get; set; }

        // True if the Windows Audio Session is currently active/inactive (not destroyed)
        public bool IsSessionAlive { get; set; } = true;

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

                    // Update timestamp here too
                    LastModified = DateTime.Now;

                    OnPropertyChanged();

                    // Actually mutes/unmutes
                    if (_audioService != null && !string.IsNullOrEmpty(Name))
                    {
                        _audioService.SetMute(Name, _isMuted);
                    }
                }
            }
        }

        // Consturctor requiring the service to be passed in
        public AudioAppModel(IAudioMixerService audioService)
        {
            _audioService = audioService;
        }

        public async Task LoadIconAsync()
        {
            if (string.IsNullOrEmpty(IconPath) || AppIcon != null) return;

            try
            {
                // Run disk I/O and extraction on a background thread
                byte[]? iconBytes = await Task.Run(() =>
                {
                    try
                    {
                        using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(IconPath);
                        if (sysIcon != null)
                        {
                            using var bmp = sysIcon.ToBitmap();
                            using var ms = new MemoryStream();
                            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            return ms.ToArray();
                        }
                    }
                    catch
                    {
                        // Ignore extraction errors
                    }
                    return null;
                });

                if (iconBytes != null)
                {
                    // Must initialize BitmapImage on the UI thread
                    var dispatcher = DispatcherQueue.GetForCurrentThread();
                    if (dispatcher != null)
                    {
                        // Since LoadIconAsync is already called from the UI thread (via UpdateCollection),
                        // we can just directly await the stream load. But just to be safe, we ensure it's on UI.
                        using var ms = new MemoryStream(iconBytes);
                        using var ras = ms.AsRandomAccessStream();
                        var bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(ras);
                        AppIcon = bitmap;
                    }
                }
            }
            catch
            {
                // Silently fail if icon extraction is denied
            }
        }

        // Code required by INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}