using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace SoundBar.Helpers
{
    public class BoolToBrushConverter : IValueConverter
    {
        public Brush? TrueBrush { get; set; }
        public Brush? FalseBrush { get; set; }

        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b ? TrueBrush : FalseBrush;
            }
            return FalseBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToFontFamilyConverter : IValueConverter
    {
        public FontFamily? TrueFont { get; set; }
        public FontFamily? FalseFont { get; set; }

        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b ? TrueFont : FalseFont;
            }
            return FalseFont;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToDoubleConverter : IValueConverter
    {
        public double TrueValue { get; set; }
        public double FalseValue { get; set; }

        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b ? TrueValue : FalseValue;
            }
            return FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}

