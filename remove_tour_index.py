import sys
import re

path = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = re.sub(r'\s*private int _tourStepIndex = -1;\s*', '\n', text)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Removed _tourStepIndex')
