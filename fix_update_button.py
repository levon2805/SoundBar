import sys

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_titlebar_btn = '''                <Button x:Name="UpdateButton"
                        Grid.Column="1"
                        Content="&#xE896;" 
                        FontFamily="{ThemeResource SymbolThemeFontFamily}"
                        Width="32" Height="32"
                        Padding="0" MinWidth="0" MinHeight="0"
                        Background="Transparent"
                        Foreground="#4CAF50"
                        BorderThickness="0"
                        FontSize="16"
                        Margin="0,0,4,0"
                        Click="UpdateBanner_Click"
                        Visibility="{x:Bind ViewModel.UpdateBannerVisibility, Mode=OneWay}"
                        ToolTipService.ToolTip="{x:Bind ViewModel.UpdateBannerText, Mode=OneWay}"/>'''

text = text.replace(old_titlebar_btn, '')

old_footer = '''            <!-- App Version (Global) -->
            <TextBlock Grid.Row="1"
                       Text="{x:Bind ViewModel.AppVersionText, Mode=OneWay}" 
                       Foreground="{ThemeResource TextFillColorDisabledBrush}" 
                       FontSize="10" 
                       HorizontalAlignment="Right" 
                       VerticalAlignment="Bottom"
                       Margin="0,0,10,5"/>'''

new_footer = '''            <!-- Footer -->
            <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,10,5" Spacing="8">
                <Button x:Name="UpdateButton"
                        Content="&#xE896;  Update Available" 
                        FontFamily="{ThemeResource SymbolThemeFontFamily}"
                        Padding="8,4"
                        Background="#1A4CAF50"
                        Foreground="#4CAF50"
                        BorderBrush="#4CAF50"
                        BorderThickness="1"
                        CornerRadius="4"
                        FontSize="10"
                        Click="UpdateBanner_Click"
                        Visibility="{x:Bind ViewModel.UpdateBannerVisibility, Mode=OneWay}"
                        ToolTipService.ToolTip="{x:Bind ViewModel.UpdateBannerText, Mode=OneWay}"/>
                
                <!-- App Version (Global) -->
                <TextBlock Text="{x:Bind ViewModel.AppVersionText, Mode=OneWay}" 
                           Foreground="{ThemeResource TextFillColorDisabledBrush}" 
                           FontSize="10" 
                           VerticalAlignment="Center"/>
            </StackPanel>'''

text = text.replace(old_footer, new_footer)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)

path2 = 'src/SoundBar/Services/UpdateService.cs'
with open(path2, 'r', encoding='utf-8') as f:
    text2 = f.read()

text2 = text2.replace('public const string CurrentVersion = "v3.3.1";', 'public const string CurrentVersion = "v4.0.0";')

with open(path2, 'w', encoding='utf-8') as f:
    f.write(text2)

print('Fixed Update Button and Version')
