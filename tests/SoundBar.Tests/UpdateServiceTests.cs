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
    public class UpdateServiceTests
    {
        private HttpMessageHandler CreateMockMessageHandler(string responseContent, HttpStatusCode statusCode)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage()
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
    }
}
