using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using SoundBar.Services;
using Xunit;

namespace SoundBar.Tests
{
    // Shares UpdateService's web client with UpdateSafetyTests, so they mustn't run at the same time
    [Collection("UpdateService")]
    public class UpdateServiceTests
    {
        private HttpMessageHandler CreateMockMessageHandler(string responseContent, HttpStatusCode statusCode)
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                // A new response each time, like a real server: the download disposes each one when it's done
                .ReturnsAsync(() => new HttpResponseMessage()
                {
                    StatusCode = statusCode,
                    Content = new StringContent(responseContent),
                })
                .Verifiable();
            return handlerMock.Object;
        }

        [Fact]
        public async Task CheckForUpdatesAsync_WhenNewerVersionExists_WithBothAssets_ReturnsTrue()
        {
            // Arrange — must include both .zip AND .sig assets to pass validation
            string json = @"{
                ""tag_name"": ""v9.9.9"",
                ""assets"": [
                    {
                        ""name"": ""SoundBar-v9.9.9.zip"",
                        ""browser_download_url"": ""https://github.com/test/download.zip""
                    },
                    {
                        ""name"": ""SoundBar-v9.9.9.sig"",
                        ""browser_download_url"": ""https://github.com/test/download.sig""
                    }
                ]
            }";
            
            var handler = CreateMockMessageHandler(json, HttpStatusCode.OK);
            UpdateService.SetTestMessageHandler(handler);
            var service = new UpdateService();

            // Act
            bool result = await service.CheckForUpdatesAsync();

            // Assert
            Assert.True(result);
            Assert.Equal("v9.9.9", service.LatestVersion);
            Assert.Equal("https://github.com/test/download.zip", service.DownloadUrl);
            Assert.Equal("https://github.com/test/download.sig", service.SignatureUrl);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_WhenNewerVersionExists_MissingSigAsset_ReturnsFalse()
        {
            // Arrange — only .zip asset, no .sig
            string json = @"{
                ""tag_name"": ""v9.9.9"",
                ""assets"": [
                    {
                        ""name"": ""SoundBar-v9.9.9.zip"",
                        ""browser_download_url"": ""https://github.com/test/download.zip""
                    }
                ]
            }";
            
            var handler = CreateMockMessageHandler(json, HttpStatusCode.OK);
            UpdateService.SetTestMessageHandler(handler);
            var service = new UpdateService();

            // Act
            bool result = await service.CheckForUpdatesAsync();

            // Assert — should refuse to update without a signature file
            Assert.False(result);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_WhenSameVersionExists_ReturnsFalse()
        {
            // Arrange — use the actual current version string
            string currentVersion = UpdateService.CurrentVersion;
            string json = $@"{{
                ""tag_name"": ""{currentVersion}"",
                ""assets"": [
                    {{
                        ""name"": ""SoundBar-{currentVersion}.zip"",
                        ""browser_download_url"": ""https://github.com/test/download.zip""
                    }},
                    {{
                        ""name"": ""SoundBar-{currentVersion}.sig"",
                        ""browser_download_url"": ""https://github.com/test/download.sig""
                    }}
                ]
            }}";
            
            var handler = CreateMockMessageHandler(json, HttpStatusCode.OK);
            UpdateService.SetTestMessageHandler(handler);
            var service = new UpdateService();

            // Act
            bool result = await service.CheckForUpdatesAsync();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_WhenApiFails_ReturnsFalseAndDoesNotCrash()
        {
            // Arrange
            var handler = CreateMockMessageHandler("", HttpStatusCode.NotFound);
            UpdateService.SetTestMessageHandler(handler);
            var service = new UpdateService();

            // Act
            bool result = await service.CheckForUpdatesAsync();

            // Assert
            Assert.False(result);
        }

        // --- New v4.1.0 Tests ---

        [Fact]
        public async Task DownloadAndApplyUpdateAsync_WhenNoUpdateIsReady_ThrowsFriendlyError()
        {
            var service = new UpdateService();

            var ex = await Assert.ThrowsAsync<UpdateFailedException>(() => service.DownloadAndApplyUpdateAsync());
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        }

        [Fact]
        public async Task DownloadAndApplyUpdateAsync_WhenSignatureIsBad_ThrowsInsteadOfInstalling()
        {
            // Arrange — a newer release whose "zip" and "sig" are both junk
            string json = @"{
                ""tag_name"": ""v9.9.9"",
                ""assets"": [
                    { ""name"": ""SoundBar-v9.9.9.zip"", ""browser_download_url"": ""https://github.com/test/download.zip"" },
                    { ""name"": ""SoundBar-v9.9.9.sig"", ""browser_download_url"": ""https://github.com/test/download.sig"" }
                ]
            }";
            UpdateService.SetTestMessageHandler(CreateMockMessageHandler(json, HttpStatusCode.OK));
            var service = new UpdateService();
            Assert.True(await service.CheckForUpdatesAsync());

            // Act & Assert — verification must fail loudly, never silently
            await Assert.ThrowsAsync<UpdateFailedException>(() => service.DownloadAndApplyUpdateAsync());
        }

        private static readonly (string Name, string Url)[] MixedRelease =
        {
            ("SoundBar-v4.6.0-linux-x64.tar.gz", "linux.tar.gz"),
            ("SoundBar-v4.6.0-linux-x64.sig", "linux.sig"),
            ("SoundBar-v4.6.0-linux-x64.zip", "linux.zip"),
            ("SoundBar-v4.6.0-windows-arm64.zip", "win-arm64.zip"),
            ("SoundBar-v4.6.0-windows-arm64.sig", "win-arm64.sig"),
            ("SoundBar-v4.6.0-windows-x64.zip", "win-x64.zip"),
            ("SoundBar-v4.6.0-windows-x64.sig", "win-x64.sig"),
        };

        [Theory]
        [InlineData("x64", "win-x64.zip", "win-x64.sig")]
        [InlineData("arm64", "win-arm64.zip", "win-arm64.sig")]
        public void PickUpdateFiles_TakesThisSystemsBuild_FromAReleaseWithSeveral(string architecture, string zip, string sig)
        {
            var (zipUrl, sigUrl) = UpdateService.PickUpdateFiles(MixedRelease, architecture);
            Assert.Equal(zip, zipUrl);
            Assert.Equal(sig, sigUrl);
        }

        [Fact]
        public void PickUpdateFiles_StillReadsOlderReleases_NamedJustSoundBarZip()
        {
            var old = new[] { ("SoundBar.sig", "old.sig"), ("SoundBar.zip", "old.zip") };
            Assert.Equal(("old.zip", "old.sig"), UpdateService.PickUpdateFiles(old, "x64"));

            // Those were only ever for Windows x64
            Assert.Equal(((string?)null, (string?)null), UpdateService.PickUpdateFiles(old, "arm64"));
        }

        [Fact]
        public void PickUpdateFiles_NeedsTheMatchingSignature_NotJustAnySig()
        {
            // Another build's signature would fail the check anyway, so treat it as no update rather than a failed one
            var files = new[] { ("SoundBar-v4.6.0-linux-x64.sig", "linux.sig"), ("SoundBar-v4.6.0-windows-x64.zip", "win-x64.zip") };
            Assert.Equal(("win-x64.zip", (string?)null), UpdateService.PickUpdateFiles(files, "x64"));
        }

        [Fact]
        public void PickUpdateFiles_IgnoresBuildsForOtherSystems()
        {
            var linuxOnly = new[] { ("SoundBar-v4.6.0-linux-x64.zip", "linux.zip"), ("SoundBar-v4.6.0-linux-x64.sig", "linux.sig") };
            Assert.Equal(((string?)null, (string?)null), UpdateService.PickUpdateFiles(linuxOnly, "x64"));
        }
    }
}
