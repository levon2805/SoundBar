using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SoundBar.Services
{
    public class MediaInfoEventArgs : EventArgs
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public IRandomAccessStreamReference? Thumbnail { get; set; }
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
        private GlobalSystemMediaTransportControlsSession? _currentSession;

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
            if (_currentSession == null) return;

            try
            {
                var timeline = _currentSession.GetTimelineProperties();
                var playback = _currentSession.GetPlaybackInfo();
                
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
            if (_currentSession != null)
            {
                try
                {
                    await _currentSession.TryChangePlaybackPositionAsync(position.Ticks);
                }
                catch { }
            }
        }

        private async void UpdateMediaProperties()
        {
            if (_currentSession == null) return;

            try
            {
                var props = await _currentSession.TryGetMediaPropertiesAsync();
                if (props != null)
                {
                    MediaInfoChanged?.Invoke(this, new MediaInfoEventArgs
                    {
                        Title = props.Title,
                        Artist = props.Artist,
                        Thumbnail = props.Thumbnail
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get media properties: {ex.Message}");
            }
        }

        public void Dispose()
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
