using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SoundBar.Services
{
    public class MediaInfoEventArgs : EventArgs
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;

        /// <summary>
        /// The album art as an image file (usually JPEG or PNG), or null if there isn't any.
        /// Read once here and shared by everyone listening, so treat it as read-only.
        /// </summary>
        public byte[]? Thumbnail { get; set; }
    }
    public class TimelineInfoEventArgs : EventArgs
    {
        public TimeSpan Position { get; set; }
        public TimeSpan EndTime { get; set; }
        public DateTimeOffset LastUpdatedTime { get; set; }
        public bool IsPlaying { get; set; }
    }

    public class MediaInfoService : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private volatile GlobalSystemMediaTransportControlsSession? _currentSession;
        private readonly object _sessionLock = new object();

        public event EventHandler<MediaInfoEventArgs>? MediaInfoChanged;
        public event EventHandler<TimelineInfoEventArgs>? TimelineInfoChanged;

        public async Task InitializeAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                
                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
                    UpdateSession();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to init MediaInfoService: {ex.Message}");
            }
        }

        public void Refresh()
        {
            UpdateSession();
        }

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            UpdateSession();
        }

        private void UpdateSession()
        {
            lock (_sessionLock)
            {
                if (_currentSession != null)
                {
                    _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                    _currentSession.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
                    _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                }

                if (_sessionManager != null)
                {
                    _currentSession = _sessionManager.GetCurrentSession();
                    
                    if (_currentSession != null)
                    {
                        _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
                        _currentSession.TimelinePropertiesChanged += CurrentSession_TimelinePropertiesChanged;
                        _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
                        UpdateMediaProperties();
                        UpdateTimelineProperties();
                    }
                    else
                    {
                        // No session active
                        MediaInfoChanged?.Invoke(this, new MediaInfoEventArgs());
                        TimelineInfoChanged?.Invoke(this, new TimelineInfoEventArgs());
                    }
                }
            }
        }

        private void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            UpdateMediaProperties();
        }

        private void CurrentSession_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            UpdateTimelineProperties();
        }

        private void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            UpdateTimelineProperties();
        }

        private void UpdateTimelineProperties()
        {
            // Read it once, since another thread can swap or clear it while we're working
            var session = _currentSession;
            if (session == null) return;

            try
            {
                var timeline = session.GetTimelineProperties();
                var playback = session.GetPlaybackInfo();
                
                TimelineInfoChanged?.Invoke(this, new TimelineInfoEventArgs
                {
                    Position = timeline.Position,
                    EndTime = timeline.EndTime,
                    LastUpdatedTime = timeline.LastUpdatedTime,
                    IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get timeline properties: {ex.Message}");
            }
        }

        public async Task SeekAsync(TimeSpan position)
        {
            var session = _currentSession;
            if (session != null)
            {
                try
                {
                    await session.TryChangePlaybackPositionAsync(position.Ticks);
                }
                catch { }
            }
        }

        // Bumped every time we start reading the song details, so only the newest read gets announced
        private int _mediaPropertiesVersion;

        /// <summary>
        /// How long to wait before each extra look for artwork that hasn't turned up yet. Browsers and some
        /// players announce the new song first and add the artwork a moment later, sometimes without saying so.
        /// </summary>
        private static readonly int[] ArtworkRetryDelaysMs = { 400, 1000, 2000 };

        /// <summary>
        /// Anything bigger than this isn't album art we want to hold on to.
        /// </summary>
        private const long MaxThumbnailBytes = 16 * 1024 * 1024;

        private async void UpdateMediaProperties()
        {
            var session = _currentSession;
            if (session == null) return;

            // Players often fire several changes in a row when the song changes. Each read takes a moment and
            // they can finish in any order, so an older read (say, from before the artwork arrived) must never
            // overwrite a newer one. Only the latest read is allowed to announce anything.
            int version = Interlocked.Increment(ref _mediaPropertiesVersion);
            bool IsStillCurrent() => version == Volatile.Read(ref _mediaPropertiesVersion) && ReferenceEquals(session, _currentSession);

            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    var props = await session.TryGetMediaPropertiesAsync();
                    if (props == null || !IsStillCurrent()) return;

                    byte[]? artwork = await ReadThumbnailAsync(props.Thumbnail);
                    if (!IsStillCurrent()) return;

                    // Announce the song straight away so the title never waits on the artwork,
                    // then again once the artwork turns up
                    if (attempt == 0 || artwork != null)
                    {
                        MediaInfoChanged?.Invoke(this, new MediaInfoEventArgs
                        {
                            Title = props.Title ?? string.Empty,
                            Artist = props.Artist ?? string.Empty,
                            Thumbnail = artwork
                        });
                    }

                    // Got it, nothing's playing, or we've waited long enough (some songs just don't have artwork)
                    if (artwork != null || string.IsNullOrEmpty(props.Title) || attempt >= ArtworkRetryDelaysMs.Length) return;

                    await Task.Delay(ArtworkRetryDelaysMs[attempt]);
                    if (!IsStillCurrent()) return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get media properties: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the album art into memory. Returns null if there isn't any, or it couldn't be read yet
        /// (players sometimes hand over an empty picture while they're still fetching the real one).
        /// </summary>
        private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? thumbnail)
        {
            if (thumbnail == null) return null;

            try
            {
                using var stream = await thumbnail.OpenReadAsync();
                if (stream.Size > MaxThumbnailBytes) return null;

                using var source = stream.AsStreamForRead();
                using var memory = new MemoryStream((int)stream.Size);
                await source.CopyToAsync(memory);

                return memory.Length > 0 ? memory.ToArray() : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't read the album art: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            lock (_sessionLock)
            {
                if (_currentSession != null)
                {
                    _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                    _currentSession.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
                    _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                    _currentSession = null;
                }

                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged -= SessionManager_CurrentSessionChanged;
                    _sessionManager = null;
                }
            }
        }
    }
}
