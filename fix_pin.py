import sys

path = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

# Add MainWindow_Activated
activated_handler = '''        private bool _hasAppliedInitialPin = false;

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (!_hasAppliedInitialPin)
            {
                _hasAppliedInitialPin = true;
                if (_settingsService.Settings.IsPinned)
                {
                    SetTopmost(true);
                    UpdatePinButtonVisual(true);
                }
            }
        }
'''

# Find the end of the constructor
import re
match = re.search(r'RestoreWindowPosition\(\);\s*\}', text)
if match:
    insert_idx = match.end()
    text = text[:insert_idx] + '\n\n' + activated_handler + text[insert_idx:]

text = text.replace('RestoreWindowPosition();', 'this.Activated += MainWindow_Activated;\n            RestoreWindowPosition();')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)

print('Added pin fix')
