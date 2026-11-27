using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // Every open player, so we notice one starting to play even when Windows doesn't make it "current"
        private readonly List<GlobalSystemMediaTransportControlsSession> _watchedSessions = new();

        // When each player last started playing, by its app ID
        private readonly Dictionary<string, DateTime> _lastStartedPlaying = new();
        private readonly Dictionary<string, bool> _wasPlaying = new();

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
                    _sessionManager.SessionsChanged += SessionManager_SessionsChanged;
                    UpdateSession(rescan: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to init MediaInfoService: {ex.Message}");
            }
        }

        public void Refresh()
        {
            UpdateSession(rescan: true, announce: true);
        }

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            UpdateSession(rescan: false);
        }

        private void SessionManager_SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            UpdateSession(rescan: true);
        }

        private void AnySession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            // A player started or stopped: that can change which one we should be showing
            UpdateSession(rescan: false);
        }

        private static string IdOf(GlobalSystemMediaTransportControlsSession session)
        {
            try { return session.SourceAppUserModelId ?? ""; } catch { return ""; }
        }

        private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
        {
            try { return session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }

        /// <summary>
        /// Works out which player to follow (see <see cref="MediaSessionPicker"/>) and switches to it if it changed.
        /// </summary>
        /// <param name="rescan">Re-read the list of players, after one opened or closed.</param>
        /// <param name="announce">Report the song details even if we're still following the same player.</param>
        private void UpdateSession(bool rescan, bool announce = false)
        {
            lock (_sessionLock)
            {
                var manager = _sessionManager;
                if (manager == null) return;

                if (rescan)
                {
                    foreach (var old in _watchedSessions)
                    {
                        try { old.PlaybackInfoChanged -= AnySession_PlaybackInfoChanged; } catch { }
                    }
                    _watchedSessions.Clear();
                    try
                    {
                        foreach (var session in manager.GetSessions())
                        {
                            session.PlaybackInfoChanged += AnySession_PlaybackInfoChanged;
                            _watchedSessions.Add(session);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Couldn't list media players: {ex.Message}");
                    }
                }

                // Note when each player starts playing
                var now = DateTime.UtcNow;
                var candidates = new List<MediaSessionCandidate>();
                foreach (var session in _watchedSessions)
                {
                    string id = IdOf(session);
                    if (id.Length == 0 || candidates.Any(c => c.Id == id)) continue;
                    bool playing = IsPlaying(session);
                    if (playing && !(_wasPlaying.TryGetValue(id, out bool was) && was)) _lastStartedPlaying[id] = now;
                    _wasPlaying[id] = playing;
                    candidates.Add(new MediaSessionCandidate(id, playing, _lastStartedPlaying.TryGetValue(id, out var t) ? t : null));
                }

                GlobalSystemMediaTransportControlsSession? windowsCurrent = null;
                try { windowsCurrent = manager.GetCurrentSession(); } catch { }
                if (candidates.Count == 0 && windowsCurrent != null)
                {
                    // Not in the list yet (it can lag behind), so go with Windows' pick
                    candidates.Add(new MediaSessionCandidate(IdOf(windowsCurrent), IsPlaying(windowsCurrent), null));
                }

                var followed = _currentSession;
                string? pickedId = MediaSessionPicker.Pick(candidates, followed == null ? null : IdOf(followed), windowsCurrent == null ? null : IdOf(windowsCurrent));

                var picked = pickedId == null ? null
                    : _watchedSessions.FirstOrDefault(s => IdOf(s) == pickedId)
                      ?? (windowsCurrent != null && IdOf(windowsCurrent) == pickedId ? windowsCurrent : null);

                // Still the same player. After the list was re-read, though, it may have closed and opened again
                // under the same name, leaving our old copy of it dead: then take the new one below.
                bool samePlayer = followed != null && pickedId != null && IdOf(followed) == pickedId;
                if (samePlayer && (!rescan || picked == null || ReferenceEquals(picked, followed)))
                {
                    if (announce)
                    {
                        UpdateMediaProperties();
                        UpdateTimelineProperties();
                    }
                    return;
                }

                if (followed != null)
                {
                    try
                    {
                        followed.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                        followed.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
                        followed.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                    }
                    catch { }
                }

                _currentSession = picked;

                if (picked != null)
                {
                    picked.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
                    picked.TimelinePropertiesChanged += CurrentSession_TimelinePropertiesChanged;
                    picked.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
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

        // --- Controlling the player ---
        // Each command goes straight to the player SoundBar is showing. Some players (Stremio, for one) ignore
        // the keyboard's media keys entirely, and with several players open the keys go to whichever one
        // Windows picks. Only if there's no player, or it says no, do we fall back to pressing the media key.

        /// <summary>Plays or pauses the player SoundBar is showing.</summary>
        public Task PlayPauseAsync() => SendAsync(async session =>
        {
            if (await session.TryTogglePlayPauseAsync()) return true;
            // Some players only take a separate play or pause
            return IsPlaying(session) ? await session.TryPauseAsync() : await session.TryPlayAsync();
        }, Helpers.MediaHelper.PlayPause);

        /// <summary>Skips to the next track in the player SoundBar is showing.</summary>
        public Task NextAsync() => SendAsync(async session => await session.TrySkipNextAsync(), Helpers.MediaHelper.NextTrack);

        /// <summary>Goes back a track in the player SoundBar is showing.</summary>
        public Task PreviousAsync() => SendAsync(async session => await session.TrySkipPreviousAsync(), Helpers.MediaHelper.PreviousTrack);

        private async Task SendAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command, Action mediaKey)
        {
            var session = _currentSession;
            bool worked = false;
            if (session != null)
            {
                try
                {
                    worked = await command(session);
                }
                catch (Exception ex)
                {
                    // The player may have just closed
                    System.Diagnostics.Debug.WriteLine($"Media command failed: {ex.Message}");
                }
            }

            string? windowsCurrentId = null;
            try
            {
                var current = _sessionManager?.GetCurrentSession();
                if (current != null) windowsCurrentId = IdOf(current);
            }
            catch { }

            if (MediaSessionPicker.ShouldPressMediaKey(session == null ? null : IdOf(session), windowsCurrentId, worked))
            {
                mediaKey();
            }
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

                foreach (var session in _watchedSessions)
                {
                    try { session.PlaybackInfoChanged -= AnySession_PlaybackInfoChanged; } catch { }
                }
                _watchedSessions.Clear();

                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged -= SessionManager_CurrentSessionChanged;
                    _sessionManager.SessionsChanged -= SessionManager_SessionsChanged;
                    _sessionManager = null;
                }
            }
        }
    }
}
