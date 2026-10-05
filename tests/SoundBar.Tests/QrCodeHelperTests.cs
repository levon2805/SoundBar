using SoundBar.Helpers;
using Xunit;

namespace SoundBar.Tests
{
    public class QrCodeHelperTests
    {
        [Fact]
        public void GeneratePng_ReturnsAValidPngImage()
        {
            byte[] png = QrCodeHelper.GeneratePng("http://192.168.1.10:6767");

            // Every PNG file starts with the same 8-byte signature
            byte[] pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Assert.True(png.Length > pngSignature.Length);
            Assert.Equal(pngSignature, png[..8]);
        }

        [Fact]
        public void GeneratePng_DifferentUrls_ProduceDifferentImages()
        {
            byte[] first = QrCodeHelper.GeneratePng("http://192.168.1.10:6767");
            byte[] second = QrCodeHelper.GeneratePng("http://192.168.1.11:6767");

            Assert.NotEqual(first, second);
        }
    }
}
