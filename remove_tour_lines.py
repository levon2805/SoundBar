import sys

path = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    lines = f.readlines()

# delete lines 405 to 659
del lines[404:659]

with open(path, 'w', encoding='utf-8') as f:
    f.writelines(lines)

print('Deleted tour lines')
