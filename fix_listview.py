import sys

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_block = '''                <ListView Grid.Row="1" 
                          Visibility="{x:Bind ViewModel.ActiveAppsVisibility, Mode=OneWay}"
                          ItemsSource="{Binding Apps}" 
                          SelectionMode="None" 
                          >
                    <ListView.ItemTemplate>'''

new_block = '''                <ListView Grid.Row="1" 
                          Visibility="{x:Bind ViewModel.ActiveAppsVisibility, Mode=OneWay}"
                          ItemsSource="{Binding Apps}" 
                          SelectionMode="None">
                    <ListView.ItemContainerStyle>
                        <Style TargetType="ListViewItem">
                            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                            <Setter Property="Padding" Value="0"/>
                            <Setter Property="Margin" Value="0"/>
                        </Style>
                    </ListView.ItemContainerStyle>
                    <ListView.ItemTemplate>'''

text = text.replace(old_block, new_block)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed ListView')
