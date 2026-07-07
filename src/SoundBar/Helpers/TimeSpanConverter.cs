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
                var ts = TimeSpan.FromSeconds(seconds);
                return ts.TotalHours >= 1
                    ? ts.ToString(@"h\:mm\:ss")
                    : ts.ToString(@"m\:ss");
            }
            return value;
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
