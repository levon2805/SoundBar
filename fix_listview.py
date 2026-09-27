import sys
import re

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

pattern = re.compile(r'<ScrollViewer Grid\.Row=\"1\" VerticalScrollBarVisibility=\"Auto\"\s*Visibility=\"\{x:Bind ViewModel\.ActiveAppsVisibility, Mode=OneWay\}\">\s*<ItemsControl ItemsSource=\"\{Binding Apps\}\">\s*<ItemsControl\.ItemTemplate>', re.DOTALL)

new_list = '''<ListView Grid.Row="1" 
                          Visibility="{x:Bind ViewModel.ActiveAppsVisibility, Mode=OneWay}"
                          ItemsSource="{Binding Apps}" 
                          SelectionMode="None" 
                          ItemContainerStyle="{StaticResource AppsListViewItemStyle}">
                    <ListView.ItemTemplate>'''

text = pattern.sub(new_list, text)

pattern2 = re.compile(r'</ItemsControl\.ItemTemplate>\s*</ItemsControl>\s*</ScrollViewer>', re.DOTALL)
new_list2 = '''</ListView.ItemTemplate>
                </ListView>'''

text = pattern2.sub(new_list2, text)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed ListView')
