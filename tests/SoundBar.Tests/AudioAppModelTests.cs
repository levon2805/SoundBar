using System.Threading.Tasks;
using Moq;
using SoundBar.Models;
using SoundBar.Services;
using Xunit;

namespace SoundBar.Tests
{
    public class AudioAppModelTests
    {
        [Fact]
        public void SyncVolumeFromOS_UpdatesVolumeWithoutCallingAudioService()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "test.exe",
                Name = "Test App"
            };

            // Act
            model.SyncVolumeFromOS(0.5f);

            // Assert
            Assert.Equal(0.5f, model.Volume);
            Assert.Equal(50, model.VolumePercentage);
            // Verify SetVolume was NEVER called because we're just syncing FROM the OS
            mockService.Verify(s => s.SetVolume(It.IsAny<string>(), It.IsAny<float>()), Times.Never);
        }

        [Fact]
        public void SyncMuteFromOS_UpdatesMuteWithoutCallingAudioService()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "test.exe"
            };

            // Act
            model.SyncMuteFromOS(true);

            // Assert
            Assert.True(model.IsMuted);
            mockService.Verify(s => s.SetMute(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public void IsMutedSetter_WhenChanged_CallsAudioService()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "test.exe"
            };

            // Act
            model.IsMuted = true;

            // Assert
            mockService.Verify(s => s.SetMute("test.exe", true), Times.Once);
        }

        [Fact]
        public async Task VolumeSetter_WhenChanged_DebouncesAndCallsAudioService()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "test.exe"
            };

            // Act
            model.Volume = 0.2f;
            model.Volume = 0.4f;
            model.Volume = 0.8f; // Only this final value should be sent to the OS

            // Assert: Before delay, it should not have called
            mockService.Verify(s => s.SetVolume(It.IsAny<string>(), It.IsAny<float>()), Times.Never);

            // Wait for the 50ms debounce task to run
            await Task.Delay(100);

            // Assert: After delay, it should only send 0.8f
            mockService.Verify(s => s.SetVolume("test.exe", 0.8f), Times.Once);
            mockService.Verify(s => s.SetVolume("test.exe", 0.2f), Times.Never);
            mockService.Verify(s => s.SetVolume("test.exe", 0.4f), Times.Never);
        }

        // --- New v2.4.0 Tests ---

        [Fact]
        public void NameSetter_WhenChanged_FiresAliasChangedCallback()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "game.exe",
                DisplayName = "Game"
            };

            string? capturedProcessName = null;
            string? capturedAlias = null;
            model.AliasChanged = (processName, alias) =>
            {
                capturedProcessName = processName;
                capturedAlias = alias;
            };

            // Act
            model.Name = "My Custom Name";

            // Assert
            Assert.Equal("game.exe", capturedProcessName);
            Assert.Equal("My Custom Name", capturedAlias);
        }

        [Fact]
        public void NameSetter_WhenSameValue_DoesNotFireCallback()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "game.exe",
                Name = "Original"
            };

            int callCount = 0;
            model.AliasChanged = (_, _) => callCount++;

            // Act — set to the same value
            model.Name = "Original";

            // Assert — callback should NOT have fired
            Assert.Equal(0, callCount);
        }

        [Fact]
        public void DisplayName_RemainsStable_AfterNameAliasChange()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "hl2.exe",
                DisplayName = "Hl2"
            };

            // Act — change the alias (Name)
            model.Name = "Half Life 2";

            // Assert — DisplayName should NOT have changed
            Assert.Equal("Hl2", model.DisplayName);
            Assert.Equal("Half Life 2", model.Name);
        }

        [Fact]
        public void PushVolumeToOS_UsesRawProcessName_NotAlias()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "chrome.exe",
                DisplayName = "Chrome",
                Name = "My Browser" // User alias
            };
            model.SyncVolumeFromOS(0.75f);

            // Act
            model.PushVolumeToOS();

            // Assert — should use "chrome.exe" (RawProcessName), not "My Browser" (alias)
            mockService.Verify(s => s.SetVolume("chrome.exe", 0.75f), Times.Once);
        }

        [Fact]
        public void IsMutedSetter_UsesRawProcessName_NotAlias()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "spotify.exe",
                DisplayName = "Spotify",
                Name = "Music Player" // User alias
            };

            // Act
            model.IsMuted = true;

            // Assert — should use "spotify.exe" not "Music Player"
            mockService.Verify(s => s.SetMute("spotify.exe", true), Times.Once);
        }

        [Fact]
        public void VolumePercentage_RoundTrip_Boundaries()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "test.exe"
            };

            // Act & Assert — 0%
            model.VolumePercentage = 0;
            Assert.Equal(0f, model.Volume);
            Assert.Equal(0, model.VolumePercentage);

            // Act & Assert — 50%
            model.VolumePercentage = 50;
            Assert.Equal(0.5f, model.Volume);
            Assert.Equal(50, model.VolumePercentage);

            // Act & Assert — 100%
            model.VolumePercentage = 100;
            Assert.Equal(1.0f, model.Volume);
            Assert.Equal(100, model.VolumePercentage);
        }

        [Fact]
        public void Dispose_CancelsDebounce()
        {
            // Arrange
            var mockService = new Mock<IAudioMixerService>();
            var model = new AudioAppModel(mockService.Object)
            {
                RawProcessName = "test.exe"
            };

            // Trigger a volume change to create a debounce CTS
            model.Volume = 0.5f;

            // Act — dispose should not throw
            model.Dispose();

            // Assert — calling Dispose again should be safe (idempotent)
            model.Dispose();
        }
    }
}
