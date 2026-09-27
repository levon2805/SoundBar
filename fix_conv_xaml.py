import sys

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_xaml = '''                <helpers:BoolToBrushConverter x:Key="FocusedToBrushConverter" 
                                             TrueBrush="#280078D7" 
                                             FalseBrush="Transparent"/>
                <helpers:BoolToBrushConverter x:Key="MutedToForegroundConverter" 
                                             TrueBrush="Red" 
                                             FalseBrush="{ThemeResource TextFillColorSecondaryBrush}"/>
                <helpers:BoolToFontFamilyConverter x:Key="MutedToFontFamilyConverter"
                                                  TrueFont="{ThemeResource SymbolThemeFontFamily}"
                                                  FalseFont="Segoe UI"/>'''

new_xaml = '''                <SolidColorBrush x:Key="FocusTrueBrush" Color="#280078D7"/>
                <SolidColorBrush x:Key="FocusFalseBrush" Color="Transparent"/>
                <SolidColorBrush x:Key="MuteTrueBrush" Color="Red"/>
                <FontFamily x:Key="SegoeFont">Segoe UI</FontFamily>

                <helpers:BoolToBrushConverter x:Key="FocusedToBrushConverter" 
                                             TrueBrush="{StaticResource FocusTrueBrush}" 
                                             FalseBrush="{StaticResource FocusFalseBrush}"/>
                <helpers:BoolToBrushConverter x:Key="MutedToForegroundConverter" 
                                             TrueBrush="{StaticResource MuteTrueBrush}" 
                                             FalseBrush="{ThemeResource TextFillColorSecondaryBrush}"/>
                <helpers:BoolToFontFamilyConverter x:Key="MutedToFontFamilyConverter"
                                                  TrueFont="{ThemeResource SymbolThemeFontFamily}"
                                                  FalseFont="{StaticResource SegoeFont}"/>'''

text = text.replace(old_xaml, new_xaml)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed Converter XAML')
