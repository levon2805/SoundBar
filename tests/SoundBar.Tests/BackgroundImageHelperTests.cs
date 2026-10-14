using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SoundBar.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Xunit;

namespace SoundBar.Tests
{
    public class BackgroundImageHelperTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), $"soundbar_bg_test_{Guid.NewGuid():N}");

        public BackgroundImageHelperTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try { Directory.Delete(_folder, true); } catch { }
        }

        /// <summary>
        /// Makes a picture with some detail in it, so the encoders have real work to do.
        /// </summary>
        private string CreateImage(string name, int width, int height, ImageFormat format, short? exifOrientation = null)
        {
            string path = Path.Combine(_folder, name);
            using var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
            {
                using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, width, height), Color.SteelBlue, Color.Orange, 30f);
                g.FillRectangle(brush, 0, 0, width, height);
                for (int x = 0; x < width; x += 97) g.DrawLine(Pens.White, x, 0, width - x, height);
            }

            if (exifOrientation.HasValue)
            {
                // PropertyItem has no public constructor, so this is the usual way to add EXIF tags
                var item = (PropertyItem)RuntimeHelpers.GetUninitializedObject(typeof(PropertyItem));
                item.Id = 0x0112; // Orientation
                item.Type = 3;    // Short
                item.Len = 2;
                item.Value = BitConverter.GetBytes(exifOrientation.Value);
                bitmap.SetPropertyItem(item);
            }

            bitmap.Save(path, format);
            return path;
        }

        private static async Task<BitmapDecoder> OpenAsync(IRandomAccessStream stream) => await BitmapDecoder.CreateAsync(stream);

        [Theory]
        [InlineData(3840u, 2160u, 500, 625, 0.2894)]  // 4K landscape in the default window: height decides
        [InlineData(1000u, 3000u, 500, 625, 0.5)]     // Tall picture: width decides
        [InlineData(400u, 300u, 500, 625, 2.0833)]    // Already smaller than the window
        public void GetCoverScale_CoversTheWholeArea(uint imageWidth, uint imageHeight, int areaWidth, int areaHeight, double expected)
        {
            double scale = BackgroundImageHelper.GetCoverScale(imageWidth, imageHeight, areaWidth, areaHeight);

            Assert.Equal(expected, scale, 3);
            Assert.True(imageWidth * scale >= areaWidth - 0.5 && imageHeight * scale >= areaHeight - 0.5);
        }

        [Theory]
        [InlineData("photo.jpg", "jpeg")]
        [InlineData("art.png", "png")]
        public async Task ShrinkAsync_MakesASmallCopyAtTheRightSize(string name, string kind)
        {
            var format = kind == "jpeg" ? ImageFormat.Jpeg : ImageFormat.Png;
            string path = CreateImage(name, 3840, 2160, format);

            using var file = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            var decoder = await OpenAsync(file);
            double scale = BackgroundImageHelper.GetCoverScale(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, 500, 625);

            using var shrunk = await BackgroundImageHelper.ShrinkAsync(decoder, scale);
            var result = await OpenAsync(shrunk);

            // Still covers the window...
            Assert.True(result.PixelWidth >= 500 && result.PixelHeight >= 625, $"{result.PixelWidth}x{result.PixelHeight}");
            // ...keeps its shape...
            Assert.Equal(3840.0 / 2160.0, (double)result.PixelWidth / result.PixelHeight, 2);
            // ...and photos stay photos (JPEG), everything else stays lossless (PNG)
            Assert.Equal(kind == "jpeg" ? BitmapDecoder.JpegDecoderId : BitmapDecoder.PngDecoderId, result.DecoderInformation.CodecId);

            // Decoded memory: full size vs shrunk (4 bytes a pixel)
            long fullBytes = 3840L * 2160 * 4;
            long shrunkBytes = (long)result.PixelWidth * result.PixelHeight * 4;
            Assert.True(shrunkBytes * 10 < fullBytes, $"{shrunkBytes} vs {fullBytes}");
        }

        [Fact]
        public async Task ShrinkAsync_TurnsSidewaysPhonePhotosTheRightWayUp()
        {
            // Stored landscape, but tagged "rotate 90 degrees", like a portrait photo straight off a phone
            string path = CreateImage("phone.jpg", 4000, 3000, ImageFormat.Jpeg, exifOrientation: 6);

            using var file = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            var decoder = await OpenAsync(file);
            Assert.Equal(3000u, decoder.OrientedPixelWidth);
            Assert.Equal(4000u, decoder.OrientedPixelHeight);

            double scale = BackgroundImageHelper.GetCoverScale(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, 500, 625);
            using var shrunk = await BackgroundImageHelper.ShrinkAsync(decoder, scale);
            var result = await OpenAsync(shrunk);

            // Comes out portrait, the right size to cover the window
            Assert.True(result.PixelHeight > result.PixelWidth, $"{result.PixelWidth}x{result.PixelHeight}");
            Assert.True(result.PixelWidth >= 500 && result.PixelHeight >= 625, $"{result.PixelWidth}x{result.PixelHeight}");
        }
    }
}
