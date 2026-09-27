import sys

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

block_to_remove = '''                        <!-- Media Controls Settings -->
                        <Expander HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderThickness="1" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
                            <Expander.Header>
                                <TextBlock Text="Media Controls" Foreground="{ThemeResource TextFillColorPrimaryBrush}" FontSize="14" FontWeight="SemiBold"/>
                            </Expander.Header>
                            <StackPanel>
                                <TextBlock Text="Shows global media controls at the bottom of the application (Previous, Play/Pause, Next, Mute)." Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12" Margin="0,0,0,10" TextWrapping="Wrap"/>
                                <ToggleSwitch Header="Show Media Controls" 
                                              IsOn="{x:Bind ViewModel.ShowMediaControls, Mode=TwoWay}" 
                                              Foreground="{ThemeResource TextFillColorPrimaryBrush}"/>
                            </StackPanel>
                        </Expander>

'''

text = text.replace(block_to_remove, '')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed Media Controls Expander')
