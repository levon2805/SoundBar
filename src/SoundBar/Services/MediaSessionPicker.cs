using System;
using System.Collections.Generic;
using System.Linq;

namespace SoundBar.Services
{
    /// <summary>
    /// One media player as far as choosing which one to show is concerned.
    /// </summary>
    /// <param name="Id">The player's app ID, like "Spotify.exe" or "stremio-shell-ng.exe".</param>
    /// <param name="IsPlaying">Whether it's playing right now.</param>
    /// <param name="LastStartedPlaying">When we last saw it start playing, or null if we haven't.</param>
    public readonly record struct MediaSessionCandidate(string Id, bool IsPlaying, DateTime? LastStartedPlaying);

    /// <summary>
    /// Decides which media player SoundBar shows and controls when more than one is open.
    /// Windows' own "current" player is often not the one you're using: a paused video app can stay current
    /// while music plays elsewhere, or the other way round.
    /// </summary>
    public static class MediaSessionPicker
    {
        /// <summary>
        /// The player to follow, in this order:
        /// 1. The one we're already showing, if it's playing (so two players at once don't make it flick back and forth).
        /// 2. Windows' current player, if it's playing.
        /// 3. Whichever player started playing most recently.
        /// 4. With nothing playing: the one we're already showing (pausing doesn't move away from it),
        ///    then the one that played most recently, then Windows' current, then any.
        /// </summary>
        /// <returns>The chosen player's ID, or null if there are none.</returns>
        public static string? Pick(IReadOnlyList<MediaSessionCandidate> sessions, string? followedId, string? windowsCurrentId)
        {
            if (sessions.Count == 0) return null;

            bool Has(string? id) => id != null && sessions.Any(s => s.Id == id);
            bool IsPlaying(string? id) => id != null && sessions.Any(s => s.Id == id && s.IsPlaying);

            if (IsPlaying(followedId)) return followedId;
            if (IsPlaying(windowsCurrentId)) return windowsCurrentId;

            var playing = sessions.Where(s => s.IsPlaying).ToList();
            if (playing.Count > 0)
                return playing.OrderByDescending(s => s.LastStartedPlaying ?? DateTime.MinValue).First().Id;

            if (Has(followedId)) return followedId;

            var lastPlayed = sessions.Where(s => s.LastStartedPlaying != null).OrderByDescending(s => s.LastStartedPlaying).FirstOrDefault();
            if (lastPlayed.Id != null) return lastPlayed.Id;

            if (Has(windowsCurrentId)) return windowsCurrentId;
            return sessions[0].Id;
        }

        /// <summary>
        /// Whether to press the keyboard media key after sending a command straight to the player didn't work.
        /// Windows sends media keys to its own current player, so the key is only pressed when there's no player
        /// to talk to, or the player shown is Windows' current one. Otherwise pressing Next while Stremio is shown
        /// (Stremio has no Next) would skip a song in another player, like Dopamine.
        /// </summary>
        public static bool ShouldPressMediaKey(string? shownId, string? windowsCurrentId, bool commandWorked)
        {
            if (shownId == null) return true;
            if (commandWorked) return false;
            return windowsCurrentId != null && string.Equals(shownId, windowsCurrentId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
