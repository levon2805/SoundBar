import sys
import re

path = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('\n    }\n}\n\n    }\n}\n', '\n    }\n}\n')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed')
