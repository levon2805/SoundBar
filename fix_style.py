import sys
import re

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('ItemContainerStyle="{StaticResource AppsListViewItemStyle}">', '>')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed Style')
