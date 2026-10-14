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
            service1.Settings.InputMuteHotkey = "Control+Shift+M";

            // Act
            service1.SaveSettings();
            var service2 = new SettingsService(_testFilePath);

            // Assert
            Assert.False(service2.Settings.ShowOutputDevice);
            Assert.False(service2.Settings.ShowInputDevice);
            Assert.False(service2.Settings.ShowMasterVolume);
            Assert.False(service2.Settings.ShowActiveApps);
            Assert.Equal("Control+Shift+M", service2.Settings.InputMuteHotkey);
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

        // --- New v4.1.0 Tests ---

        [Fact]
        public void Load_DefaultSettings_WindowIsFullyOpaqueAndShortcutNotYetMade()
        {
            var service = new SettingsService(_testFilePath);

            Assert.Equal(100, service.Settings.WindowOpacity);
            Assert.False(service.Settings.HasCreatedDesktopShortcut);
        }

        [Fact]
        public void SaveAndLoad_WindowOpacityAndShortcutFlag_PersistCorrectly()
        {
            var service1 = new SettingsService(_testFilePath);
            service1.Settings.WindowOpacity = 65;
            service1.Settings.HasCreatedDesktopShortcut = true;

            service1.SaveSettings();
            var service2 = new SettingsService(_testFilePath);

            Assert.Equal(65, service2.Settings.WindowOpacity);
            Assert.True(service2.Settings.HasCreatedDesktopShortcut);
        }

        [Theory]
        [InlineData(0, 30)]
        [InlineData(-50, 30)]
        [InlineData(29, 30)]
        [InlineData(250, 100)]
        public void Load_HandEditedOpacityOutOfRange_IsClampedSoWindowStaysVisible(int savedValue, int expected)
        {
            File.WriteAllText(_testFilePath, $"{{ \"WindowOpacity\": {savedValue} }}");

            var service = new SettingsService(_testFilePath);

            Assert.Equal(expected, service.Settings.WindowOpacity);
        }

        [Fact]
        public void Save_WhenItSucceeds_HasNoSaveError()
        {
            var service = new SettingsService(_testFilePath);

            service.SaveSettings();

            Assert.Null(service.LastSaveError);
            Assert.True(File.Exists(_testFilePath));
        }

        // --- New v4.1.2 Tests ---

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(70000)]
        public void Load_HandEditedPortOutOfRange_FallsBackToDefault(int savedPort)
        {
            File.WriteAllText(_testFilePath, $"{{ \"CompanionServerPort\": {savedPort} }}");

            var service = new SettingsService(_testFilePath);

            Assert.Equal(6767, service.Settings.CompanionServerPort);
        }

        [Fact]
        public void Load_HandEditedNullList_DoesNotLeaveANull()
        {
            File.WriteAllText(_testFilePath, "{ \"HiddenApps\": null, \"AppAliases\": null }");

            var service = new SettingsService(_testFilePath);

            Assert.NotNull(service.Settings.HiddenApps);
            Assert.NotNull(service.Settings.AppAliases);
        }

        /// <summary>
        /// Starts watching, and hands back a task that finishes with the settings from the next hand edit
        /// (or null if nothing turns up in time).
        /// </summary>
        private static System.Threading.Tasks.Task<Models.AppSettings?> WatchForEditAsync(SettingsService service, int timeoutMs = 3000)
        {
            var edited = new System.Threading.Tasks.TaskCompletionSource<Models.AppSettings?>();
            service.ChangedExternally += settings => edited.TrySetResult(settings);
            service.StartWatchingForExternalChanges();

            _ = System.Threading.Tasks.Task.Delay(timeoutMs).ContinueWith(_ => edited.TrySetResult(null));
            return edited.Task;
        }

        [Fact]
        public async System.Threading.Tasks.Task EditingTheFileByHand_IsPickedUpWhileRunning()
        {
            var service = new SettingsService(_testFilePath);
            service.SaveSettings();
            using (service)
            {
                var edit = WatchForEditAsync(service);

                // Like someone changing the port in Notepad and saving
                string json = File.ReadAllText(_testFilePath).Replace("\"CompanionServerPort\": 6767", "\"CompanionServerPort\": 8123");
                File.WriteAllText(_testFilePath, json);

                var edited = await edit;
                Assert.NotNull(edited);
                Assert.Equal(8123, edited!.CompanionServerPort);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task OurOwnSaves_AreNotMistakenForHandEdits()
        {
            var service = new SettingsService(_testFilePath);
            service.SaveSettings();
            using (service)
            {
                var edit = WatchForEditAsync(service, timeoutMs: 1500);

                service.Settings.WindowOpacity = 55;
                service.SaveSettings();

                Assert.Null(await edit);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task HalfFinishedHandEdit_IsIgnored()
        {
            var service = new SettingsService(_testFilePath);
            service.Settings.CompanionServerPort = 7000;
            service.SaveSettings();
            using (service)
            {
                var edit = WatchForEditAsync(service, timeoutMs: 1500);

                File.WriteAllText(_testFilePath, "{ \"CompanionServerPort\": 80");

                // No event, and we keep what we had rather than resetting to defaults
                Assert.Null(await edit);
                Assert.Equal(7000, service.Settings.CompanionServerPort);
            }
        }
    }
}
