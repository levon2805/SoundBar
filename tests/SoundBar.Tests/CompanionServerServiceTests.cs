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
            Assert.Equal("SoundBar Companion - All Networks (6767)", CompanionServerService.GetFirewallRuleName(6767));
            Assert.NotEqual(CompanionServerService.GetFirewallRuleName(6767), CompanionServerService.GetFirewallRuleName(8080));
        }

        // --- New v4.1.1 Tests ---

        [Fact]
        public void GetFirewallRuleName_DiffersFromThe410Rule()
        {
            // 4.1.0 users already have a Private-only rule. If the names matched, we'd find that rule,
            // think we were done, and Public networks would stay blocked.
            Assert.Equal("SoundBar Companion (6767)", CompanionServerService.GetLegacyFirewallRuleName(6767));
            Assert.NotEqual(CompanionServerService.GetLegacyFirewallRuleName(6767), CompanionServerService.GetFirewallRuleName(6767));
        }

        // --- v4.2.0: album art is sent with the right picture type ---

        [Theory]
        [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
        [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
        [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }, "image/gif")]
        [InlineData(new byte[] { (byte)'B', (byte)'M', 0, 0 }, "image/bmp")]
        [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, "image/webp")]
        [InlineData(new byte[] { 1, 2, 3 }, "image/jpeg")]
        [InlineData(new byte[0], "image/jpeg")]
        public void GetImageContentType_RecognisesCommonPictureTypes(byte[] bytes, string expected)
        {
            Assert.Equal(expected, CompanionServerService.GetImageContentType(bytes));
        }
    }
}
