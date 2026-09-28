import sys

path = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_block = '''        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {

            if (ViewModel != null)
            {
                ViewModel.ThemeChanged -= ViewModel_ThemeChanged;
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Dispose();
            }
        }'''

new_block = '''        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            SaveWindowSettings();

            SongPositionSlider.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(SongPositionSlider_PointerPressed));

            if (ViewModel != null)
            {
                ViewModel.ThemeChanged -= ViewModel_ThemeChanged;
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Dispose();
            }
        }'''

text = text.replace(old_block, new_block)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed H2 H3')
