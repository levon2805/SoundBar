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

        // --- New v2.4.0 Tests ---

        [Fact]
        public void SaveAndLoad_AppAliases_PersistsCorrectly()
        {
            // Arrange
            var service1 = new SettingsService(_testFilePath);
            service1.Settings.AppAliases["chrome.exe"] = "Browser";
            service1.Settings.AppAliases["hl2.exe"] = "Half Life 2";

            // Act
            service1.SaveSettings();
            var service2 = new SettingsService(_testFilePath);

            // Assert
            Assert.Equal(2, service2.Settings.AppAliases.Count);
            Assert.Equal("Browser", service2.Settings.AppAliases["chrome.exe"]);
            Assert.Equal("Half Life 2", service2.Settings.AppAliases["hl2.exe"]);
        }

        [Fact]
        public void SaveAndLoad_RunAtStartup_PersistsCorrectly()
        {
            // Arrange
            var service1 = new SettingsService(_testFilePath);
            service1.Settings.RunAtStartup = true;

            // Act
            service1.SaveSettings();
            var service2 = new SettingsService(_testFilePath);

            // Assert
            Assert.True(service2.Settings.RunAtStartup);
        }

        [Fact]
        public void Load_DefaultSettings_HasEmptyAppAliases()
        {
            // Arrange & Act
            var service = new SettingsService(_testFilePath);

            // Assert
            Assert.NotNull(service.Settings.AppAliases);
            Assert.Empty(service.Settings.AppAliases);
        }

        [Fact]
        public void Load_DefaultSettings_RunAtStartupIsFalse()
        {
            // Arrange & Act
            var service = new SettingsService(_testFilePath);

            // Assert
            Assert.False(service.Settings.RunAtStartup);
        }

        // --- New v3.0.0 Tests ---

        [Fact]
        public void SaveAndLoad_CompanionServerSettings_PersistsCorrectly()
        {
            // Arrange
            var service1 = new SettingsService(_testFilePath);
            service1.Settings.EnableCompanionServer = true;
            service1.Settings.CompanionServerPort = 8080;
            service1.Settings.ShowMediaControls = false;

            // Act
            service1.SaveSettings();
            var service2 = new SettingsService(_testFilePath);

            // Assert
            Assert.True(service2.Settings.EnableCompanionServer);
            Assert.Equal(8080, service2.Settings.CompanionServerPort);
            Assert.False(service2.Settings.ShowMediaControls);
        }

        [Fact]
        public void Load_DefaultSettings_CompanionServerIsConfiguredProperly()
        {
            // Arrange & Act
            var service = new SettingsService(_testFilePath);

            // Assert
            Assert.False(service.Settings.EnableCompanionServer); // Default off
            Assert.Equal(6767, service.Settings.CompanionServerPort); // Default port
            Assert.True(service.Settings.ShowMediaControls); // Default on
        }

        // --- New v3.1.0 Tests ---

        [Fact]
        public void SaveAndLoad_LayoutSettings_PersistsCorrectly()
        {
            // Arrange
            var service1 = new SettingsService(_testFilePath);
            service1.Settings.ShowOutputDevice = false;
            service1.Settings.ShowInputDevice = false;
            service1.Settings.ShowMasterVolume = false;
            service1.Settings.ShowActiveApps = false;

            // Act
            service1.SaveSettings();
            var service2 = new SettingsService(_testFilePath);

            // Assert
            Assert.False(service2.Settings.ShowOutputDevice);
            Assert.False(service2.Settings.ShowInputDevice);
            Assert.False(service2.Settings.ShowMasterVolume);
            Assert.False(service2.Settings.ShowActiveApps);
        }

        [Fact]
        public void Load_DefaultSettings_LayoutAndHotkeysAreConfiguredProperly()
        {
            // Arrange & Act
            var service = new SettingsService(_testFilePath);

            // Assert
            Assert.True(service.Settings.ShowOutputDevice);
            Assert.True(service.Settings.ShowInputDevice);
            Assert.True(service.Settings.ShowMasterVolume);
            Assert.True(service.Settings.ShowActiveApps);
            Assert.Equal("Control+Alt+I", service.Settings.InputMuteHotkey);
        }
    }
}
