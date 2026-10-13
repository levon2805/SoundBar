using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace SoundBar.Helpers
{
    public class BoolToBrushConverter : Microsoft.UI.Xaml.DependencyObject, IValueConverter
    {
        public Brush? TrueBrush
        {
            get => (Brush?)GetValue(TrueBrushProperty);
            set => SetValue(TrueBrushProperty, value);
        }
        public static readonly Microsoft.UI.Xaml.DependencyProperty TrueBrushProperty =
            Microsoft.UI.Xaml.DependencyProperty.Register(nameof(TrueBrush), typeof(Brush), typeof(BoolToBrushConverter), new Microsoft.UI.Xaml.PropertyMetadata(null));

        public Brush? FalseBrush
        {
            get => (Brush?)GetValue(FalseBrushProperty);
            set => SetValue(FalseBrushProperty, value);
        }
        public static readonly Microsoft.UI.Xaml.DependencyProperty FalseBrushProperty =
            Microsoft.UI.Xaml.DependencyProperty.Register(nameof(FalseBrush), typeof(Brush), typeof(BoolToBrushConverter), new Microsoft.UI.Xaml.PropertyMetadata(null));

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

    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                if (Invert) b = !b;
                return b ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            }
            return Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}

