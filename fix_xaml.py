import sys

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

expanders_xaml = '''
                        <!-- About & Updates Expander -->
                        <Expander HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderThickness="1" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
                            <Expander.Header>
                                <TextBlock Text="About &amp; Updates" Foreground="{ThemeResource TextFillColorPrimaryBrush}" FontSize="14" FontWeight="SemiBold"/>
                            </Expander.Header>
                            <StackPanel Spacing="15">
                                <TextBlock Text="Check out the latest features and changes in this version." FontSize="12" TextWrapping="Wrap"/>
                                <Button Content="View Release Notes" Click="VersionHyperlink_Click"/>
                            </StackPanel>
                        </Expander>

                        <!-- Global Hotkeys Expander -->
                        <Expander HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderThickness="1" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
                            <Expander.Header>
                                <TextBlock Text="Global Hotkeys" Foreground="{ThemeResource TextFillColorPrimaryBrush}" FontSize="14" FontWeight="SemiBold"/>
                            </Expander.Header>
                            <StackPanel Spacing="15">
                                <TextBlock Text="Control the volume of the app you are currently using without leaving it." FontSize="12" TextWrapping="Wrap"/>
                                <StackPanel Margin="0,0,0,10">
                                    <TextBlock Text="Volume Up Hotkey (Active App)" Margin="0,0,0,5"/>
                                    <Button HorizontalAlignment="Stretch" Content="{x:Bind ViewModel.VolumeUpHotkey, Mode=OneWay}" Click="RecordVolumeUpHotkey_Click"/>
                                </StackPanel>
                                <StackPanel Margin="0,0,0,10">
                                    <TextBlock Text="Volume Down Hotkey (Active App)" Margin="0,0,0,5"/>
                                    <Button HorizontalAlignment="Stretch" Content="{x:Bind ViewModel.VolumeDownHotkey, Mode=OneWay}" Click="RecordVolumeDownHotkey_Click"/>
                                </StackPanel>
                                <StackPanel Margin="0,0,0,10">
                                    <TextBlock Text="Mute Hotkey (Active App)" Margin="0,0,0,5"/>
                                    <Button HorizontalAlignment="Stretch" Content="{x:Bind ViewModel.MuteHotkey, Mode=OneWay}" Click="RecordMuteHotkey_Click"/>
                                </StackPanel>
                                <StackPanel Margin="0,0,0,10">
                                    <TextBlock Text="Mute Microphone Hotkey" Margin="0,0,0,5"/>
                                    <Button HorizontalAlignment="Stretch" Content="{x:Bind ViewModel.InputMuteHotkey, Mode=OneWay}" Click="RecordInputMuteHotkey_Click"/>
                                </StackPanel>
                            </StackPanel>
                        </Expander>

                        <!-- Do Not Disturb Mode Expander -->
                        <Expander HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderThickness="1" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
                            <Expander.Header>
                                <TextBlock Text="Do Not Disturb Mode" Foreground="{ThemeResource TextFillColorPrimaryBrush}" FontSize="14" FontWeight="SemiBold"/>
                            </Expander.Header>
                            <StackPanel Spacing="15">
                                <TextBlock Text="Mutes all system sounds and notifications when enabled." FontSize="12" TextWrapping="Wrap"/>
                                <ToggleSwitch OnContent="Enabled" OffContent="Disabled" IsOn="{x:Bind ViewModel.IsDoNotDisturbEnabled, Mode=TwoWay}" Toggled="DndToggleButton_Changed"/>
                            </StackPanel>
                        </Expander>

                        <!-- Layout Settings -->
                        <Expander HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderThickness="1" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
                            <Expander.Header>
                                <TextBlock Text="Layout Settings" Foreground="{ThemeResource TextFillColorPrimaryBrush}" FontSize="14" FontWeight="SemiBold"/>
                            </Expander.Header>
                            <StackPanel Spacing="10">
                                <TextBlock Text="Toggle which sections are visible on the main page. Hidden sections are still functional via hotkeys." FontSize="12" TextWrapping="Wrap"/>
                                <ToggleSwitch Header="Output Device Picker" OnContent="Visible" OffContent="Hidden" IsOn="{x:Bind ViewModel.ShowOutputDevice, Mode=TwoWay}"/>
                                <ToggleSwitch Header="Input Device (Microphone)" OnContent="Visible" OffContent="Hidden" IsOn="{x:Bind ViewModel.ShowInputDevice, Mode=TwoWay}"/>
                                <ToggleSwitch Header="Master Volume Slider" OnContent="Visible" OffContent="Hidden" IsOn="{x:Bind ViewModel.ShowMasterVolume, Mode=TwoWay}"/>
                                <ToggleSwitch Header="Active Apps List" OnContent="Visible" OffContent="Hidden" IsOn="{x:Bind ViewModel.ShowActiveApps, Mode=TwoWay}"/>
                                <ToggleSwitch Header="Media Controls" OnContent="Visible" OffContent="Hidden" IsOn="{x:Bind ViewModel.ShowMediaControls, Mode=TwoWay}"/>
                                <ToggleSwitch Header="Feature Tour Button" OnContent="Visible" OffContent="Hidden" IsOn="{x:Bind ViewModel.ShowFeatureTour, Mode=TwoWay}" Toggled="FeatureTourToggle_Toggled"/>
                            </StackPanel>
                        </Expander>

                        <!-- System Sounds Expander -->
                        <Expander HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderThickness="1" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
                            <Expander.Header>
                                <TextBlock Text="System Sounds" Foreground="{ThemeResource TextFillColorPrimaryBrush}" FontSize="14" FontWeight="SemiBold"/>
                            </Expander.Header>
                            <StackPanel Spacing="15">
                                <TextBlock Text="Opens the Windows Sound settings where you can manage sound schemes, change notification sounds, and configure programme event audio." FontSize="12" TextWrapping="Wrap"/>
                                <Button Content="Open System Sounds" Click="OpenSystemSounds_Click"/>
                            </StackPanel>
                        </Expander>
'''

text = text.replace('<!-- Theme Settings -->', expanders_xaml + '\n                          <!-- Theme Settings -->')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('XAML Inserted')
