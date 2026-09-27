import sys

path = 'src/SoundBar/Helpers/Converters.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('public class BoolToBrushConverter : IValueConverter\n    {\n        public Brush? TrueBrush { get; set; }\n        public Brush? FalseBrush { get; set; }', '''public class BoolToBrushConverter : Microsoft.UI.Xaml.DependencyObject, IValueConverter
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
            Microsoft.UI.Xaml.DependencyProperty.Register(nameof(FalseBrush), typeof(Brush), typeof(BoolToBrushConverter), new Microsoft.UI.Xaml.PropertyMetadata(null));''')

text = text.replace('public class BoolToFontFamilyConverter : IValueConverter\n    {\n        public FontFamily? TrueFont { get; set; }\n        public FontFamily? FalseFont { get; set; }', '''public class BoolToFontFamilyConverter : Microsoft.UI.Xaml.DependencyObject, IValueConverter
    {
        public FontFamily? TrueFont
        {
            get => (FontFamily?)GetValue(TrueFontProperty);
            set => SetValue(TrueFontProperty, value);
        }
        public static readonly Microsoft.UI.Xaml.DependencyProperty TrueFontProperty =
            Microsoft.UI.Xaml.DependencyProperty.Register(nameof(TrueFont), typeof(FontFamily), typeof(BoolToFontFamilyConverter), new Microsoft.UI.Xaml.PropertyMetadata(null));

        public FontFamily? FalseFont
        {
            get => (FontFamily?)GetValue(FalseFontProperty);
            set => SetValue(FalseFontProperty, value);
        }
        public static readonly Microsoft.UI.Xaml.DependencyProperty FalseFontProperty =
            Microsoft.UI.Xaml.DependencyProperty.Register(nameof(FalseFont), typeof(FontFamily), typeof(BoolToFontFamilyConverter), new Microsoft.UI.Xaml.PropertyMetadata(null));''')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed Converters')
