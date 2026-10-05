using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Moq;
using SoundBar.Models;
using SoundBar.Services;
using Xunit;

namespace SoundBar.Tests
{
    public class CompanionServerServiceTests
    {
        [Fact]
        public void GetLocalIpAddress_ReturnsValidIpFormat()
        {
            // Act
            string? ip = CompanionServerService.GetLocalIpAddress();

            // Assert
            // It might be null on a machine without network, but on most CI/CD it returns a string
            if (ip != null)
            {
                Assert.Matches(@"^(\d{1,3}\.){3}\d{1,3}$", ip);
            }
        }

        // --- New v4.1.0 Tests ---

        private static CompanionServerService CreateServer(IEnumerable<AudioAppModel> apps)
        {
            var audio = new Mock<IAudioMixerService>().Object;
            return new CompanionServerService(
                audio,
                new MediaInfoService(),
                () => apps,
                () => new List<AudioDeviceModel>(),
                () => null,
                _ => { },
                () => new List<AudioDeviceModel>(),
                () => null,
                _ => { });
        }

        private static AudioAppModel App(string iconPath) =>
            new AudioAppModel(new Mock<IAudioMixerService>().Object) { RawProcessName = "app.exe", IconPath = iconPath };

        [Fact]
        public void IsKnownAppIconPath_AllowsIconsForAppsInTheMixer()
        {
            using var server = CreateServer(new[] { App(@"C:\Program Files\Spotify\Spotify.exe") });

            Assert.True(server.IsKnownAppIconPath(@"C:\Program Files\Spotify\Spotify.exe"));
            Assert.True(server.IsKnownAppIconPath(@"c:\program files\spotify\spotify.exe")); // Windows paths are case-insensitive
        }

        [Fact]
        public void IsKnownAppIconPath_RefusesFilesThatArentInTheMixer()
        {
            using var server = CreateServer(new[] { App(@"C:\Program Files\Spotify\Spotify.exe") });

            Assert.False(server.IsKnownAppIconPath(@"C:\Windows\System32\cmd.exe"));
        }

        [Theory]
        [InlineData(@"\\attacker\share\evil.exe")]
        [InlineData("//attacker/share/evil.exe")]
        public void IsKnownAppIconPath_AlwaysRefusesNetworkPaths(string path)
        {
            // Even if a network path somehow ended up in the mixer, we never touch it
            using var server = CreateServer(new[] { App(path) });

            Assert.False(server.IsKnownAppIconPath(path));
        }

        [Fact]
        public void GetFirewallRuleName_IncludesThePort()
        {
            // So that changing the port creates a new rule instead of reusing a stale one
            Assert.Equal("SoundBar Companion (6767)", CompanionServerService.GetFirewallRuleName(6767));
            Assert.NotEqual(CompanionServerService.GetFirewallRuleName(6767), CompanionServerService.GetFirewallRuleName(8080));
        }
    }
}
