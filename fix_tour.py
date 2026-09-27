import sys
import re

path = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

pattern = re.compile(r'\s*var tourRow = \(Grid\)this\.FindName\(\"tourRow\"\);.*?\}\s*\}', re.DOTALL)
if pattern.search(text):
    text = pattern.sub('\n            }\n        }', text)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(text)
    print('Fixed FeatureTourToggle_Toggled')
else:
    print('Not found')
