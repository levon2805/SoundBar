using System.Collections.Generic;
using SoundBar.Helpers;
using Xunit;

namespace SoundBar.Tests
{
    public class NetworkHelperTests
    {
        [Fact]
        public void ShouldTreatAsPublic_WarnsOnPublicWifi()
        {
            var networks = new List<NetworkInfo> { new(true, NetworkCategory.Public) };

            Assert.True(NetworkHelper.ShouldTreatAsPublic(networks));
        }

        [Theory]
        [InlineData(NetworkCategory.Private)]
        [InlineData(NetworkCategory.Domain)]
        public void ShouldTreatAsPublic_StaysQuietOnTrustedNetworks(NetworkCategory category)
        {
            var networks = new List<NetworkInfo> { new(true, category) };

            Assert.False(NetworkHelper.ShouldTreatAsPublic(networks));
        }

        [Fact]
        public void ShouldTreatAsPublic_IgnoresOfflineVirtualAdapters()
        {
            // WSL/Hyper-V adapters show up as Public "Unidentified network" with no internet.
            // They shouldn't trigger a warning when the real network is Private.
            var networks = new List<NetworkInfo>
            {
                new(true, NetworkCategory.Private),
                new(false, NetworkCategory.Public)
            };

            Assert.False(NetworkHelper.ShouldTreatAsPublic(networks));
        }

        [Fact]
        public void ShouldTreatAsPublic_ChecksEverythingWhenNothingIsOnline()
        {
            // e.g. stuck behind a hotel login page, so Windows doesn't see internet yet
            var networks = new List<NetworkInfo> { new(false, NetworkCategory.Public) };

            Assert.True(NetworkHelper.ShouldTreatAsPublic(networks));
        }

        [Fact]
        public void ShouldTreatAsPublic_StaysQuietWithNoNetworks()
        {
            Assert.False(NetworkHelper.ShouldTreatAsPublic(new List<NetworkInfo>()));
        }
    }
}
