using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SoundBar.Helpers;
using SoundBar.Models;
using SoundBar.Services;
using Xunit;

namespace SoundBar.Tests
{
    /// <summary>Which media player SoundBar shows and controls when several are open.</summary>
    public class MediaSessionPickerTests
    {
        private static readonly DateTime T0 = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

        private static MediaSessionCandidate S(string id, bool playing, int? startedMinutesAgo = null) =>
            new(id, playing, startedMinutesAgo == null ? null : T0.AddMinutes(-startedMinutesAgo.Value));

        [Fact]
        public void StremioAlone_IsShown_EvenWhenPaused()
        {
            var sessions = new[] { S("stremio-shell-ng.exe", playing: false) };
            Assert.Equal("stremio-shell-ng.exe", MediaSessionPicker.Pick(sessions, null, "stremio-shell-ng.exe"));
            Assert.Equal("stremio-shell-ng.exe", MediaSessionPicker.Pick(sessions, null, null));
        }

        [Fact]
        public void APlayingPlayer_WinsOverWindowsPausedPick()
        {
            // What happened on your PC: Stremio playing, but Windows kept a paused Dopamine as "current"
            var sessions = new[] { S("Dopamine.exe", false, 30), S("stremio-shell-ng.exe", true, 1) };
            Assert.Equal("stremio-shell-ng.exe", MediaSessionPicker.Pick(sessions, "Dopamine.exe", "Dopamine.exe"));
        }

        [Fact]
        public void PausingDoesntMoveAway_FromThePlayerBeingShown()
        {
            var sessions = new[] { S("Dopamine.exe", false, 30), S("stremio-shell-ng.exe", false, 1) };
            Assert.Equal("stremio-shell-ng.exe", MediaSessionPicker.Pick(sessions, "stremio-shell-ng.exe", "Dopamine.exe"));
        }

        [Fact]
        public void TwoPlayingAtOnce_StaysWithTheOneShown_SoItDoesntFlickBetweenThem()
        {
            var sessions = new[] { S("Spotify.exe", true, 10), S("chrome.exe", true, 1) };
            Assert.Equal("Spotify.exe", MediaSessionPicker.Pick(sessions, "Spotify.exe", "chrome.exe"));
        }

        [Fact]
        public void WhenTheShownPlayerStops_AndAnotherIsPlaying_SwitchesToTheOtherOne()
        {
            var sessions = new[] { S("Spotify.exe", false, 10), S("chrome.exe", true, 1) };
            Assert.Equal("chrome.exe", MediaSessionPicker.Pick(sessions, "Spotify.exe", "Spotify.exe"));
        }

        [Fact]
        public void SeveralPlaying_NoneShownYet_PrefersWindowsPick_ThenTheNewest()
        {
            var sessions = new[] { S("a.exe", true, 10), S("b.exe", true, 1), S("c.exe", true, 5) };
            Assert.Equal("c.exe", MediaSessionPicker.Pick(sessions, null, "c.exe"));
            Assert.Equal("b.exe", MediaSessionPicker.Pick(sessions, null, "gone.exe"));
        }

        [Fact]
        public void NothingPlaying_NoneShownYet_TakesTheLastToPlay_ThenWindowsPick()
        {
            Assert.Equal("b.exe", MediaSessionPicker.Pick(new[] { S("a.exe", false, 10), S("b.exe", false, 2) }, null, "a.exe"));
            Assert.Equal("a.exe", MediaSessionPicker.Pick(new[] { S("a.exe", false), S("b.exe", false) }, null, "a.exe"));
        }

        [Fact]
        public void TheMediaKey_IsOnlyPressed_WhenItCantReachTheWrongPlayer()
        {
            // No player to talk to: the key is all there is
            Assert.True(MediaSessionPicker.ShouldPressMediaKey(null, null, commandWorked: false));
            Assert.True(MediaSessionPicker.ShouldPressMediaKey(null, "Dopamine.exe", commandWorked: false));

            // The player took the command
            Assert.False(MediaSessionPicker.ShouldPressMediaKey("stremio-shell-ng.exe", "stremio-shell-ng.exe", commandWorked: true));

            // Stremio shown, no Next, but Windows' current player is Dopamine: the key would skip Dopamine's song
            Assert.False(MediaSessionPicker.ShouldPressMediaKey("stremio-shell-ng.exe", "Dopamine.exe", commandWorked: false));

            // The shown player is Windows' current one, so the key goes to the right place
            Assert.True(MediaSessionPicker.ShouldPressMediaKey("Spotify.exe", "Spotify.exe", commandWorked: false));
        }

        [Fact]
        public void TheShownPlayerClosing_MovesOn_AndNoPlayersMeansNone()
        {
            Assert.Equal("b.exe", MediaSessionPicker.Pick(new[] { S("b.exe", false) }, "closed.exe", null));
            Assert.Null(MediaSessionPicker.Pick(Array.Empty<MediaSessionCandidate>(), "closed.exe", "closed.exe"));
        }
    }

    public class KeyedSerialQueueTests
    {
        [Fact]
        public async Task VolumeChangesForOneApp_LandInTheOrderTheyWereMade_EvenIfAnEarlierOneIsSlow()
        {
            var queue = new KeyedSerialQueue();
            var applied = new ConcurrentQueue<int>();

            var slow = queue.Enqueue("chrome.exe", () => { Thread.Sleep(150); applied.Enqueue(1); });
            var fast = queue.Enqueue("chrome.exe", () => applied.Enqueue(2));
            await Task.WhenAll(slow, fast).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { 1, 2 }, applied.ToArray());
        }

        [Fact]
        public async Task DifferentApps_DontWaitForEachOther()
        {
            var queue = new KeyedSerialQueue();
            using var release = new ManualResetEventSlim();
            var blocked = queue.Enqueue("a.exe", () => release.Wait(TimeSpan.FromSeconds(5)));

            await queue.Enqueue("b.exe", () => { }).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(blocked.IsCompleted);
            release.Set();
            await blocked.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task AFailedChange_DoesntStopTheNextOne()
        {
            var queue = new KeyedSerialQueue();
            bool ran = false;
            _ = queue.Enqueue("a.exe", () => throw new InvalidOperationException("session went away"));
            await queue.Enqueue("a.exe", () => ran = true).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(ran);
        }

        [Fact]
        public async Task AFastSliderDrag_SendsTheNewestVolumeLast()
        {
            var sent = new ConcurrentQueue<float>();
            var audio = new Mock<IAudioMixerService>();
            audio.Setup(a => a.SetVolume(It.IsAny<string>(), It.IsAny<float>()))
                 .Callback<string, float>((_, v) => sent.Enqueue(v));
            var model = new AudioAppModel(audio.Object) { RawProcessName = "chrome.exe", Name = "Chrome" };

            for (int i = 1; i <= 20; i++)
            {
                model.Volume = i / 20f;
                if (i % 3 == 0) await Task.Delay(55); // Sometimes long enough for a send to go out mid-drag
            }
            await Task.Delay(400);

            Assert.NotEmpty(sent);
            Assert.Equal(1f, sent.ToArray()[^1]);
        }
    }

    public class CompanionMediaKeyTests
    {
        [Fact]
        public void PicturesNeedTheKeyOnlyPairedPhonesAreGiven()
        {
            Assert.True(CompanionServerService.HasMediaKey("ABC123", "ABC123"));
            Assert.False(CompanionServerService.HasMediaKey(null, "ABC123"));
            Assert.False(CompanionServerService.HasMediaKey("", "ABC123"));
            Assert.False(CompanionServerService.HasMediaKey("ABC124", "ABC123"));
            Assert.False(CompanionServerService.HasMediaKey("ABC12", "ABC123"));
        }

        [Fact]
        public void TheAddressesSentToPhones_CarryTheKey()
        {
            Assert.Equal("/api/albumart?id=abc&k=KEY", CompanionServerService.MakeAlbumArtUrl("abc", "KEY"));
            Assert.Equal("/api/icon?path=C%3A%5CApps%5Ca%20b.exe&k=KEY", CompanionServerService.MakeIconUrl(@"C:\Apps\a b.exe", "KEY"));
        }
    }

    public class MuteAccessibilityTests
    {
        [Fact]
        public void TheSliderSaysMuted_AndTheIconSaysWhatClickingItDoes()
        {
            var model = new AudioAppModel(new Mock<IAudioMixerService>().Object) { RawProcessName = "firefox.exe", Name = "Firefox" };
            Assert.Equal("Firefox", model.SliderLabel);
            Assert.Equal("Mute Firefox", model.MuteButtonLabel);

            var changed = new List<string?>();
            model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            model.IsMuted = true;
            Assert.Equal("Firefox, muted", model.SliderLabel);
            Assert.Equal("Unmute Firefox", model.MuteButtonLabel);
            Assert.Contains(nameof(AudioAppModel.SliderLabel), changed);

            // Muted from Windows' own mixer too
            model.SyncMuteFromOS(false);
            Assert.Equal("Firefox", model.SliderLabel);
            Assert.Equal("Mute Firefox", model.MuteButtonLabel);
        }
    }
}
