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
    /// <summary>
    /// Represents a single audio application within the mixer.
    /// This model handles its own volume debouncing to prevent hammering the Windows Audio API.
    /// </summary>
    public class AudioAppModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IAudioMixerService _audioService;
        private readonly DispatcherQueue? _dispatcherQueue;

        /// <summary>
        /// The OS-level process ID.
        /// </summary>
        public int ProcessId { get; set; }

        private string? _name;
        /// <summary>
        /// The name displayed in the UI. Users can edit this to give their apps custom nicknames.
        /// </summary>
        public string? Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                    AliasChanged?.Invoke(RawProcessName ?? "", value ?? "");
                }
            }
        }

        /// <summary>
        /// The original, untouched display name straight from Windows.
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// Fired whenever the user decides to give an application a cheeky new nickname.
        /// </summary>
        public Action<string, string>? AliasChanged { get; set; }

        /// <summary>
        /// The raw executable name (e.g. 'stremio-shell-ng.exe'). 
        /// We use this to reliably match sessions even if they get renamed.
        /// </summary>
        public string? RawProcessName { get; set; }

        /// <summary>
        /// Where we can find the executable to grab a nice icon from.
        /// </summary>
        public string? IconPath { get; set; }

        private Microsoft.UI.Xaml.Media.ImageSource? _appIcon;
        /// <summary>
        /// The visual icon loaded for the UI.
        /// </summary>
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

        /// <summary>
        /// Tracks the last time the user fiddled with this app's volume slider.
        /// Helps us know when to stop auto-syncing from Windows to avoid fighting the user.
        /// </summary>
        public DateTime LastModified { get; private set; } = DateTime.MinValue;

        private float _volume;
        private System.Threading.CancellationTokenSource? _volumeDebounce;

        /// <summary>
        /// The current volume level, ranging from 0.0 to 1.0.
        /// Modifying this will automatically debounce and tell Windows to change the volume.
        /// </summary>
        public float Volume
        {
            get => _volume;
            set
            {
                // Only do the hard work if the volume actually changed.
                if (_volume != value)
                {
                    _volume = value;

                    // Note down the time so we don't immediately overwrite the user's change.
                    LastModified = DateTime.Now;

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VolumePercentage));

                    // Cancel any pending volume changes so we don't spam the OS while sliding.
                    if (_volumeDebounce != null)
                    {
                        _volumeDebounce.Cancel();
                        _volumeDebounce.Dispose();
                    }
                    
                    _volumeDebounce = new System.Threading.CancellationTokenSource();
                    var token = _volumeDebounce.Token;
                    var capturedVolume = _volume;
                    string osName = RawProcessName ?? Name ?? "";
                    
                    if (_audioService != null && !string.IsNullOrEmpty(osName))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                // Wait just a tiny bit to see if the user is still sliding.
                                await Task.Delay(50, token);
                                if (!token.IsCancellationRequested)
                                {
                                    _audioService.SetVolume(osName, capturedVolume);
                                }
                            }
                            catch (TaskCanceledException) { }
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Updates our volume state directly from Windows without triggering a write-back.
        /// This stops an endless loop of us telling Windows the volume, and Windows telling us back.
        /// </summary>
        /// <param name="volume">The volume level (0.0 to 1.0).</param>
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
        /// Quietly updates our mute state from Windows without causing a scene.
        /// </summary>
        /// <param name="isMuted">Whether the app is currently muted.</param>
        public void SyncMuteFromOS(bool isMuted)
        {
            if (_isMuted != isMuted)
            {
                _isMuted = isMuted;
                OnPropertyChanged(nameof(IsMuted));
            }
        }

        /// <summary>
        /// Shoves our current volume down to the Windows Audio Service.
        /// Brilliant for when an app completely destroys and recreates its audio session.
        /// </summary>
        public void PushVolumeToOS()
        {
            string osName = RawProcessName ?? DisplayName ?? "";
            if (_audioService != null && !string.IsNullOrEmpty(osName))
            {
                _audioService.SetVolume(osName, _volume);
            }
        }

        /// <summary>
        /// A friendly percentage representation of the volume, perfect for UI bindings.
        /// </summary>
        public int VolumePercentage
        {
            get => (int)Math.Round(_volume * 100);
            set
            {
                Volume = value / 100f;
            }
        }

        /// <summary>
        /// True if this seems to be a sneaky background process without a proper window.
        /// </summary>
        public bool IsBackgroundApp { get; set; }

        private bool _isFocused;
        /// <summary>
        /// True if this application is currently the active foreground window.
        /// </summary>
        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                if (_isFocused != value)
                {
                    _isFocused = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Lets us know if the Windows Audio Session is still breathing.
        /// </summary>
        public bool IsSessionAlive { get; set; } = true;

        private bool _isMuted;

        /// <summary>
        /// Mutes or unmutes the application, instantly telling Windows about the change.
        /// </summary>
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    LastModified = DateTime.Now;

                    OnPropertyChanged();

                    // Actually tell Windows to shut it up (or let it sing).
                    string osName = RawProcessName ?? DisplayName ?? "";
                    if (_audioService != null && !string.IsNullOrEmpty(osName))
                    {
                        _audioService.SetMute(osName, _isMuted);
                    }
                }
            }
        }

        /// <summary>
        /// Creates a new instance of our audio app model.
        /// </summary>
        /// <param name="audioService">The service we use to boss around the Windows audio.</param>
        public AudioAppModel(IAudioMixerService audioService)
        {
            _audioService = audioService;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }

        /// <summary>
        /// Attempts to extract a lovely icon from the application's executable.
        /// We do the heavy lifting in the background to keep the UI smooth.
        /// </summary>
        public async Task LoadIconAsync()
        {
            if (string.IsNullOrEmpty(IconPath) || AppIcon != null) return;

            try
            {
                // Pop onto a background thread for disk reads.
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
                        // Some apps are notoriously stubborn about their icons. We'll just ignore them.
                    }
                    return null;
                });

                if (iconBytes != null)
                {
                    // Hop back to the UI thread to construct the actual image.
                    if (_dispatcherQueue != null)
                    {
                        _dispatcherQueue.TryEnqueue(async () =>
                        {
                            try
                            {
                                using var ms = new MemoryStream(iconBytes);
                                using var ras = ms.AsRandomAccessStream();
                                var bitmap = new BitmapImage();
                                await bitmap.SetSourceAsync(ras);
                                AppIcon = bitmap;
                            }
                            catch { }
                        });
                    }
                }
            }
            catch
            {
                // Access denied or something similar. No big deal.
            }
        }

        /// <summary>
        /// Cleans up any pending volume tasks when this model is tossed away.
        /// </summary>
        public void Dispose()
        {
            _volumeDebounce?.Cancel();
            _volumeDebounce?.Dispose();
            _volumeDebounce = null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}