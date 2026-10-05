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
            await Task.Delay(500); 

            // Assert initial state: devices are selected
            Assert.NotNull(viewModel.SelectedAudioDevice);
            Assert.Equal("out1", viewModel.SelectedAudioDevice?.Id);
            Assert.NotNull(viewModel.SelectedInputDevice);
            Assert.Equal("in1", viewModel.SelectedInputDevice?.Id);

            // Act: Simulate user unplugging devices (empty lists returned)
            mockAudioService.Setup(s => s.GetAudioDevices()).Returns(new List<AudioDeviceModel>());
            mockAudioService.Setup(s => s.GetInputDevices()).Returns(new List<AudioDeviceModel>());

            // Wait for the next poll cycle to pick up the empty lists
            await Task.Delay(500);

            // Assert final state: selections are cleared to show placeholder text
            Assert.Null(viewModel.SelectedAudioDevice);
            Assert.Null(viewModel.SelectedInputDevice);

            viewModel.Dispose();
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
