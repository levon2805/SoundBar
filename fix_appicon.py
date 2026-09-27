import sys
import re

path = 'src/SoundBar/Models/AudioAppModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('private Microsoft.UI.Xaml.Media.ImageSource? _appIcon;', 'private string? _appIcon;')
text = text.replace('public Microsoft.UI.Xaml.Media.ImageSource? AppIcon', 'public string? AppIcon')
text = text.replace('private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _iconCache = new();', '''private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _iconCache = new();
        private static readonly string _iconDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SoundBarIcons");''')

text = text.replace('private readonly DispatcherQueue? _dispatcherQueue;', '')
pattern = re.compile(r'\s*try\s*\{\s*_dispatcherQueue = DispatcherQueue\.GetForCurrentThread\(\);\s*\}\s*catch \(Exception\)\s*\{\s*// In a unit test environment without a UI thread, DispatcherQueue may throw a COMException\.\s*_dispatcherQueue = null;\s*\}', re.DOTALL)
text = pattern.sub('', text)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed')
