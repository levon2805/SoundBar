using System;
using Microsoft.UI.Xaml.Data;

namespace SoundBar.Helpers
{
    /// <summary>
    /// A neat little XAML converter that turns raw seconds into a lovely readable time format.
    /// </summary>
    public class TimeSpanConverter : IValueConverter
    {
        /// <summary>
        /// Converts the raw double (seconds) coming from the slider into a nice string.
        /// If it's a marathon podcast over an hour, it shows the hours too.
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double seconds)
            {
                return Format(seconds);
            }
            return value;
        }

        // 1000 hours. Some live streams report a never-ending duration, which would overflow TimeSpan.
        private const double MaxDisplayableSeconds = 3_600_000;

        /// <summary>
        /// Turns seconds into m:ss, or h:mm:ss once it's an hour or more. Nonsense values show as 0:00.
        /// </summary>
        public static string Format(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;

            var ts = TimeSpan.FromSeconds(Math.Min(seconds, MaxDisplayableSeconds));

            // Total hours rather than the "h" format, which wraps back to 0 after a day
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                : ts.ToString(@"m\:ss");
        }

        /// <summary>
        /// We only ever need to read the time, not write it back as a string, so we leave this blank.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
