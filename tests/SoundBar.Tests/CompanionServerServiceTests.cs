using System;
using System.Collections.ObjectModel;
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
    }
}
