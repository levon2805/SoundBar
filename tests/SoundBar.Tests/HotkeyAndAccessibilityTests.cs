using System.Collections.Generic;
using Moq;
using SoundBar.Models;
using SoundBar.Services;
using Windows.System;
using Xunit;

namespace SoundBar.Tests
{
    public class HotkeyRuleTests
    {
        [Theory]
        [InlineData("Control+Alt+M")]
        [InlineData("Control+M")]
        [InlineData("Alt+Up")]
        [InlineData("Control+Shift+Down")]
        [InlineData("F13")]
        [InlineData("Shift+F24")]
        public void AllowsHotkeysThatDontGetInTheWayOfTyping(string hotkey)
        {
            Assert.True(HotkeyRules.IsAllowed(hotkey));
        }

        [Theory]
        [InlineData("M")]
        [InlineData("Space")]
        [InlineData("Shift+M")]
        [InlineData("F5")]
        [InlineData("F1")]
        [InlineData("F25")]
        [InlineData("Escape")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("Control+")]
        public void RefusesKeysThatWouldBeSwallowedWhileTyping(string? hotkey)
        {
            Assert.False(HotkeyRules.IsAllowed(hotkey));
        }

        [Fact]
        public void AltGrTyping_AndWindowsShortcuts_AreNotForHotkeys()
        {
            var ctrlAlt = HotkeyModifiers.Control | HotkeyModifiers.Alt;
            Assert.False(new HotkeyEventArgs(VirtualKey.M, ctrlAlt).IsNotForHotkeys);
            Assert.True(new HotkeyEventArgs(VirtualKey.M, ctrlAlt, typesAltGrCharacter: true).IsNotForHotkeys);
            Assert.True(new HotkeyEventArgs(VirtualKey.M, ctrlAlt, windowsKeyHeld: true).IsNotForHotkeys);
        }

        [Fact]
        public void TheRecordingDialog_OnlyLetsYouSaveAllowedKeys()
        {
            var audio = new Mock<IAudioMixerService>();
            audio.Setup(a => a.GetActiveAudioSessions()).Returns(new List<AudioSessionData>());
            audio.Setup(a => a.GetAudioDevices()).Returns(new List<AudioDeviceModel>());
            audio.Setup(a => a.GetInputDevices()).Returns(new List<AudioDeviceModel>());
            string settingsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SoundBarHotkeyTest_{System.Guid.NewGuid():N}.json");
            try
            {
                using var viewModel = new SoundBar.ViewModels.MainViewModel(new SettingsService(settingsPath), audio.Object);

                viewModel.IsRecordingHotkey = true;
                Assert.False(viewModel.CanSaveRecordedHotkey);
                Assert.Equal("", viewModel.RecordedHotkeyHint);

                viewModel.RecordedHotkeyString = "M";
                Assert.False(viewModel.CanSaveRecordedHotkey);
                Assert.Contains("Ctrl or Alt", viewModel.RecordedHotkeyHint);

                viewModel.RecordedHotkeyString = "Control+Alt+M";
                Assert.True(viewModel.CanSaveRecordedHotkey);
                Assert.Equal("", viewModel.RecordedHotkeyHint);
            }
            finally
            {
                try { System.IO.File.Delete(settingsPath); } catch { }
            }
        }
    }

    public class AccessibilityNameTests
    {
        [Fact]
        public void EachHideButton_SaysWhichAppItHides_AndFollowsARename()
        {
            var model = new AudioAppModel(new Mock<IAudioMixerService>().Object) { Name = "Firefox" };
            Assert.Equal("Hide Firefox", model.HideLabel);

            var changed = new List<string?>();
            model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            model.Name = "Browser";

            Assert.Equal("Hide Browser", model.HideLabel);
            Assert.Contains(nameof(AudioAppModel.HideLabel), changed);
        }

        [Fact]
        public void AHideButtonWithNoName_StillHasOne()
        {
            Assert.Equal("Hide app", new AudioAppModel(new Mock<IAudioMixerService>().Object).HideLabel);
        }
    }
}
