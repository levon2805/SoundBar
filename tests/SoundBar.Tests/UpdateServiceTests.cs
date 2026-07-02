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
        public async Task CheckForUpdatesAsync_WhenNewerVersionExists_ReturnsTrue()
        {
            // Arrange
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

            // Assert
            Assert.True(result);
            Assert.Equal("v9.9.9", service.LatestVersion);
            Assert.Equal("https://github.com/test/download.zip", service.DownloadUrl);
        }

        [Fact]
        public async Task CheckForUpdatesAsync_WhenSameVersionExists_ReturnsFalse()
        {
            // Arrange
            // Using CurrentVersion to simulate no update
            string currentVersion = UpdateService.CurrentVersion;
            string json = $@"""tag_name"": ""{currentVersion}"",
                ""assets"": [
                    {{
                        ""name"": ""SoundBar-{currentVersion}.zip"",
                        ""browser_download_url"": ""https://github.com/test/download.zip""
                    }}
                ]
            }}";
            // Fix json structure
            json = "{" + json;
            
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
