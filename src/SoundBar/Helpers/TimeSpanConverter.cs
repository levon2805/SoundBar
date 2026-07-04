using System;
using Microsoft.UI.Xaml.Data;

namespace SoundBar.Helpers
{
    public class TimeSpanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double seconds)
            {
                return TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
