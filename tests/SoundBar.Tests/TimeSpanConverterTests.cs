using System;
using SoundBar.Helpers;
using Xunit;

namespace SoundBar.Tests
{
    public class TimeSpanConverterTests
    {
        private readonly TimeSpanConverter _converter = new();

        [Fact]
        public void Convert_ZeroSeconds_ReturnsZeroColon00()
        {
            var result = _converter.Convert(0.0, typeof(string), null!, "en");
            Assert.Equal("0:00", result);
        }

        [Fact]
        public void Convert_65Seconds_Returns1Colon05()
        {
            var result = _converter.Convert(65.0, typeof(string), null!, "en");
            Assert.Equal("1:05", result);
        }

        [Fact]
        public void Convert_3661Seconds_Returns1Colon01Colon01()
        {
            // 1 hour, 1 minute, 1 second — should use h:mm:ss format
            var result = _converter.Convert(3661.0, typeof(string), null!, "en");
            Assert.Equal("1:01:01", result);
        }

        [Fact]
        public void Convert_3599Seconds_Returns59Colon59()
        {
            // Just under 1 hour — should still use m:ss format
            var result = _converter.Convert(3599.0, typeof(string), null!, "en");
            Assert.Equal("59:59", result);
        }

        [Fact]
        public void Convert_3600Seconds_Returns1Colon00Colon00()
        {
            // Exactly 1 hour boundary — should switch to h:mm:ss
            var result = _converter.Convert(3600.0, typeof(string), null!, "en");
            Assert.Equal("1:00:00", result);
        }

        [Fact]
        public void Convert_NonDouble_ReturnsValueUnchanged()
        {
            var result = _converter.Convert("not a number", typeof(string), null!, "en");
            Assert.Equal("not a number", result);
        }
    }
}
