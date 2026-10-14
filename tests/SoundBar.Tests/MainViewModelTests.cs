using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SoundBar.Models;
using SoundBar.Services;
using SoundBar.ViewModels;
using Xunit;

namespace SoundBar.Tests
{
    public class MainViewModelTests
    {
        [Fact]
        public async Task PollSystemAudioState_WhenDevicesDisconnected_ClearsSelectedDevices()
        {
            // Arrange
            var mockAudioService = new Mock<IAudioMixerService>();
            
            // We can pass null or mocks for these if we don't strictly need them to be mocked for this test,
            // but let's mock what we can or just use the concrete SettingsService if needed.
            // Wait, MainViewModel internal constructor expects:
            // SettingsService settingsService, IAudioMixerService audioService, UpdateService updateService = null, MediaInfoService mediaInfoService = null, HotkeyService hotkeyService = null
            
            // We need a real SettingsService or a mocked wrapper, but SettingsService doesn't have an interface.
            // Since it takes a filepath, we can just pass a dummy one.
            var dummySettings = new SettingsService("dummy_path.json");

            var initialOutputDevices = new List<AudioDeviceModel>
            {
                new AudioDeviceModel { Id = "out1", Name = "Speakers", IsDefault = true }
            };
            var initialInputDevices = new List<AudioDeviceModel>
            {
                new AudioDeviceModel { Id = "in1", Name = "Microphone", IsDefault = true }
            };

            // Setup audio service to return our mock devices initially
            mockAudioService.Setup(s => s.GetAudioDevices()).Returns(initialOutputDevices);
            mockAudioService.Setup(s => s.GetInputDevices()).Returns(initialInputDevices);
            mockAudioService.Setup(s => s.GetActiveAudioSessions()).Returns(new List<AudioSessionData>());

            var viewModel = new MainViewModel(
                dummySettings,
                mockAudioService.Object);

            // Give the polling loop a moment to pick up the initial devices
            await WaitUntilAsync(() => viewModel.SelectedAudioDevice != null && viewModel.SelectedInputDevice != null);

            // Assert initial state: devices are selected
            Assert.NotNull(viewModel.SelectedAudioDevice);
            Assert.Equal("out1", viewModel.SelectedAudioDevice?.Id);
            Assert.NotNull(viewModel.SelectedInputDevice);
            Assert.Equal("in1", viewModel.SelectedInputDevice?.Id);

            // Act: Simulate user unplugging devices (empty lists returned)
            mockAudioService.Setup(s => s.GetAudioDevices()).Returns(new List<AudioDeviceModel>());
            mockAudioService.Setup(s => s.GetInputDevices()).Returns(new List<AudioDeviceModel>());

            // Wait for the next poll cycle to pick up the empty lists
            await WaitUntilAsync(() => viewModel.SelectedAudioDevice == null && viewModel.SelectedInputDevice == null);

            // Assert final state: selections are cleared to show placeholder text
            Assert.Null(viewModel.SelectedAudioDevice);
            Assert.Null(viewModel.SelectedInputDevice);

            viewModel.Dispose();
        }

        /// <summary>
        /// The poll runs by itself once a second on another thread, so wait for it to deliver rather than
        /// guessing a delay. A fixed wait could land halfway through a poll and catch it half done.
        /// </summary>
        private static async Task WaitUntilAsync(System.Func<bool> condition, int timeoutMs = 3000)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(25);
            }
        }

        // --- New v4.1.0 Tests ---

        private static (MainViewModel ViewModel, SettingsService Settings, string Path) CreateViewModel()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"test_vm_{System.Guid.NewGuid()}.json");
            var settings = new SettingsService(path);

            var mockAudioService = new Mock<IAudioMixerService>();
            mockAudioService.Setup(s => s.GetAudioDevices()).Returns(new List<AudioDeviceModel>());
            mockAudioService.Setup(s => s.GetInputDevices()).Returns(new List<AudioDeviceModel>());
            mockAudioService.Setup(s => s.GetActiveAudioSessions()).Returns(new List<AudioSessionData>());

            return (new MainViewModel(settings, mockAudioService.Object), settings, path);
        }

        [Theory]
        [InlineData(65, 65)]
        [InlineData(10, 30)]   // Can't go below 30%, or the window could vanish
        [InlineData(150, 100)]
        [InlineData(72.6, 73)] // Slider values are rounded to whole percentages
        public void WindowOpacity_IsClampedAndSaved(double requested, int expected)
        {
            var (viewModel, settings, path) = CreateViewModel();
            try
            {
                viewModel.WindowOpacity = requested;

                Assert.Equal(expected, viewModel.WindowOpacity);
                Assert.Equal($"{expected}%", viewModel.WindowOpacityText);

                // Saving waits for the slider to settle, and closing the app saves anything still waiting
                viewModel.Dispose();
                Assert.Equal(expected, new SettingsService(path).Settings.WindowOpacity);
            }
            finally
            {
                viewModel.Dispose();
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void ResetWindowOpacity_GoesBackToFullySolid()
        {
            var (viewModel, settings, path) = CreateViewModel();
            try
            {
                viewModel.WindowOpacity = 50;
                viewModel.ResetWindowOpacity();

                Assert.Equal(100, viewModel.WindowOpacity);
            }
            finally
            {
                viewModel.Dispose();
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        // --- New v4.1.2 Tests ---

        [Fact]
        public async Task DraggingOpacitySlider_SavesTheFinalValueOnceItSettles()
        {
            var (viewModel, settings, path) = CreateViewModel();
            try
            {
                // A drag sends lots of values in quick succession
                for (int value = 90; value >= 40; value -= 5)
                {
                    viewModel.WindowOpacity = value;
                }

                await WaitUntilAsync(() => System.IO.File.Exists(path) && new SettingsService(path).Settings.WindowOpacity == 40);
                Assert.Equal(40, new SettingsService(path).Settings.WindowOpacity);
            }
            finally
            {
                viewModel.Dispose();
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void PinAndCompanionButtons_AreVisibleByDefault()
        {
            var (viewModel, settings, path) = CreateViewModel();
            try
            {
                Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, viewModel.PinButtonVisibility);
                Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, viewModel.CompanionButtonVisibility);
            }
            finally
            {
                viewModel.Dispose();
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void HidingPinAndCompanionButtons_IsSavedAndCollapsesThem()
        {
            var (viewModel, settings, path) = CreateViewModel();
            try
            {
                viewModel.ShowPinButton = false;
                viewModel.ShowCompanionButton = false;

                Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.PinButtonVisibility);
                Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.CompanionButtonVisibility);

                var reloaded = new SettingsService(path).Settings;
                Assert.False(reloaded.ShowPinButton);
                Assert.False(reloaded.ShowCompanionButton);
            }
            finally
            {
                viewModel.Dispose();
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void SettingsEditedByHand_AreAppliedAndNotOverwritten()
        {
            var (viewModel, settings, path) = CreateViewModel();
            try
            {
                var fresh = new AppSettings
                {
                    CompanionServerPort = 8123,
                    ShowPinButton = false,
                    HiddenApps = new List<string> { "Spotify" }
                };

                viewModel.ApplySettingsEditedByHand(fresh);

                Assert.Same(fresh, settings.Settings);
                Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.PinButtonVisibility);
                Assert.Equal(new[] { "Spotify" }, viewModel.HiddenApps);
                Assert.Contains(":8123", viewModel.CompanionServerUrl);

                // The next save (closing the window, say) keeps the edit rather than writing the old values back
                settings.SaveSettings();
                Assert.Equal(8123, new SettingsService(path).Settings.CompanionServerPort);
            }
            finally
            {
                viewModel.Dispose();
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void CloseCompanionView_ReturnsToMixer()
        {
            var (viewModel, settings, path) = CreateViewModel();
            try
            {
                viewModel.IsCompanionViewMode = true;
                Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.MixerViewVisibility);

                viewModel.IsCompanionViewMode = false;
                Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, viewModel.MixerViewVisibility);
            }
            finally
            {
                viewModel.Dispose();
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }
    }
}
