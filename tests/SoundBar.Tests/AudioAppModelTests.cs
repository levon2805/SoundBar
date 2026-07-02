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
    }
}
