import sys
import re

path = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

pattern1 = re.compile(r'TitleBarGrid\.PointerPressed \+= TitleBarGrid_PointerPressed;\s*TitleBarGrid\.PointerMoved \+= TitleBarGrid_PointerMoved;\s*TitleBarGrid\.PointerReleased \+= TitleBarGrid_PointerReleased;\s*TitleBarGrid\.PointerCanceled \+= TitleBarGrid_PointerCanceled;', re.DOTALL)
text = pattern1.sub('this.ExtendsContentIntoTitleBar = true;\n            this.SetTitleBar(TitleBarGrid);', text)

pattern2 = re.compile(r'// Drag state variables.*?// Triggered if the drag is canceled by the system\s*private void TitleBarGrid_PointerCanceled\(object sender, PointerRoutedEventArgs e\)\s*\{\s*if \(_isDragging\)\s*\{\s*_isDragging = false;\s*\(\(UIElement\)sender\)\.ReleasePointerCapture\(e\.Pointer\);\s*\}\s*\}', re.DOTALL)
text = pattern2.sub('', text)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed TitleBar')
