import sys

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_button = '''                            <!-- Huge Power Button -->
                            <ToggleButton IsChecked="{x:Bind ViewModel.EnableCompanionServer, Mode=TwoWay}"
                                          Width="120" Height="120" CornerRadius="60"
                                          Background="{ThemeResource ControlFillColorDefaultBrush}"
                                          BorderBrush="{ThemeResource ControlStrokeColorDefaultBrush}"
                                          BorderThickness="2"
                                          HorizontalAlignment="Center">
                                <TextBlock Text="&#xE7E8;" 
                                           FontFamily="{ThemeResource SymbolThemeFontFamily}" 
                                           FontSize="50" 
                                           Foreground="{ThemeResource TextFillColorPrimaryBrush}"/>
                            </ToggleButton>'''

new_button = '''                            <!-- Huge Power Button -->
                            <Button Width="120" Height="120" CornerRadius="60"
                                    Background="{ThemeResource ControlFillColorDefaultBrush}"
                                    BorderBrush="{ThemeResource ControlStrokeColorDefaultBrush}"
                                    BorderThickness="2"
                                    HorizontalAlignment="Center"
                                    Click="CompanionPowerOn_Click">
                                <TextBlock Text="&#xE7E8;" 
                                           FontFamily="{ThemeResource SymbolThemeFontFamily}" 
                                           FontSize="50" 
                                           Foreground="{ThemeResource TextFillColorPrimaryBrush}"/>
                            </Button>'''

text = text.replace(old_button, new_button)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)

path2 = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path2, 'r', encoding='utf-8') as f:
    text2 = f.read()

old_code = '''        private void CompanionPowerOff_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.StopCompanionServer();
        }'''

new_code = '''        private void CompanionPowerOn_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EnableCompanionServer = true;
        }

        private void CompanionPowerOff_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EnableCompanionServer = false;
        }'''

text2 = text2.replace(old_code, new_code)

with open(path2, 'w', encoding='utf-8') as f:
    f.write(text2)

print('Fixed Power Button')
