using System;
using System.IO;
using SoundBar.Services;
using Xunit;

namespace SoundBar.Tests
{
    public class SettingsServiceTests : IDisposable
    {
        private readonly string _testFilePath;

        public SettingsServiceTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid()}.json");
        }

        public void Dispose()
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
        }

        [Fact]
        public void Load_WhenFileDoesNotExist_ReturnsDefaultSettings()
        {
            // Arrange
            var service = new SettingsService(_testFilePath);

            // Act
            var settings = service.Settings;

            // Assert
            Assert.NotNull(settings);
            Assert.False(settings.IsPinned);
            Assert.Empty(settings.HiddenApps);
            Assert.Empty(settings.AllowedBackgroundApps);
            Assert.Empty(settings.Presets);
        }

        [Fact]
        public void SaveAndLoad_PersistsSettingsCorrectly()
        {
            // Arrange
            var service1 = new SettingsService(_testFilePath);
            service1.Settings.IsPinned = true;
            service1.Settings.HiddenApps.Add("Spotify.exe");
            
            // Act
            service1.SaveSettings();

            var service2 = new SettingsService(_testFilePath);

            // Assert
            Assert.True(service2.Settings.IsPinned);
            Assert.Single(service2.Settings.HiddenApps);
            Assert.Equal("Spotify.exe", service2.Settings.HiddenApps[0]);
        }

        [Fact]
        public void Load_WhenFileIsCorrupt_ReturnsDefaultSettingsAndDoesNotCrash()
        {
            // Arrange
            File.WriteAllText(_testFilePath, "{ invalid json structure ");
            
            // Act
            var service = new SettingsService(_testFilePath);

            // Assert
            Assert.NotNull(service.Settings);
            Assert.False(service.Settings.IsPinned); // Default state
        }
    }
}
