import sys
import re

path = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

pattern = re.compile(r'\s*private void BuildDynamicUI\(\).*?private void FindVisualChildren<T>\(DependencyObject parent, System\.Collections\.Generic\.List<T> results\) where T : DependencyObject\s*\{[^}]+\}[^}]+\}\s*\}', re.DOTALL)
if pattern.search(text):
    text = pattern.sub('\n    }\n}', text)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(text)
    print('Deleted')
else:
    print('Failed')
