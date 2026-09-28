import sys

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('Background="{Binding IsFocused, Converter={StaticResource FocusedToBrushConverter}}"', 'Background="{Binding IsHighlightVisible, Converter={StaticResource FocusedToBrushConverter}}"')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed Highlight XAML')
