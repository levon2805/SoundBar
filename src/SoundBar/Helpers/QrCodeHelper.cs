using QRCoder;

namespace SoundBar.Helpers
{
    /// <summary>
    /// Makes QR codes right here on the PC, so the companion URL (which has the user's
    /// local IP address in it) never gets sent off to an online QR service.
    /// </summary>
    public static class QrCodeHelper
    {
        /// <summary>
        /// Turns some text (e.g. the companion URL) into a black-on-white QR code PNG.
        /// </summary>
        public static byte[] GeneratePng(string text, int pixelsPerModule = 10)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data);
            return png.GetGraphic(pixelsPerModule);
        }
    }
}
