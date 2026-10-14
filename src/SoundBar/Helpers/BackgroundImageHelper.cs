using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SoundBar.Helpers
{
    /// <summary>
    /// Shrinks custom backgrounds down to the size they're actually shown at. A full-size photo is huge in
    /// memory once decoded (an 8K wallpaper is over 130 MB), but the window never shows more than a few
    /// hundred thousand pixels of it, so a small, well-scaled copy looks exactly the same.
    /// </summary>
    internal static class BackgroundImageHelper
    {
        /// <summary>
        /// JPEG quality for shrunk photos. High enough that you can't tell, especially under the dimming overlay.
        /// </summary>
        public const float JpegQuality = 0.92f;

        /// <summary>
        /// How much a picture needs scaling to just cover an area, the way UniformToFill draws it.
        /// 1 or more means it's already no bigger than it needs to be.
        /// </summary>
        public static double GetCoverScale(uint imageWidth, uint imageHeight, int areaWidth, int areaHeight)
        {
            if (imageWidth == 0 || imageHeight == 0) return 1;
            return Math.Max((double)areaWidth / imageWidth, (double)areaHeight / imageHeight);
        }

        /// <summary>
        /// Scales the picture down with Windows' high quality (Fant) filter and squashes it into a small
        /// in-memory file: JPEG for photos, lossless PNG for everything else so transparency survives.
        /// Phone photos are turned the right way up along the way.
        /// </summary>
        public static async Task<InMemoryRandomAccessStream> ShrinkAsync(BitmapDecoder decoder, double scale)
        {
            bool isJpeg = decoder.DecoderInformation.CodecId == BitmapDecoder.JpegDecoderId;

            // Scaling happens before the photo's rotation is applied, so this uses the stored (unrotated) size
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            using var pixels = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                isJpeg ? BitmapAlphaMode.Ignore : BitmapAlphaMode.Straight,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage); // Same colours as the full-size picture showed before

            var output = new InMemoryRandomAccessStream();
            try
            {
                BitmapEncoder encoder;
                if (isJpeg)
                {
                    var options = new BitmapPropertySet
                    {
                        { "ImageQuality", new BitmapTypedValue(JpegQuality, Windows.Foundation.PropertyType.Single) }
                    };
                    encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, options);
                }
                else
                {
                    encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                }

                encoder.SetSoftwareBitmap(pixels);
                await encoder.FlushAsync();
                output.Seek(0);
                return output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }
}
