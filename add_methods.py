import sys
import re

path = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

methods = '''
        private void RecordVolumeUpHotkey_Click(object sender, RoutedEventArgs e) => RecordHotkey_Click("VolumeUpHotkey", "Edit Volume Up Hotkey");
        private void RecordVolumeDownHotkey_Click(object sender, RoutedEventArgs e) => RecordHotkey_Click("VolumeDownHotkey", "Edit Volume Down Hotkey");
        private void RecordMuteHotkey_Click(object sender, RoutedEventArgs e) => RecordHotkey_Click("MuteHotkey", "Edit Mute Hotkey");
        private void RecordInputMuteHotkey_Click(object sender, RoutedEventArgs e) => RecordHotkey_Click("InputMuteHotkey", "Edit Mute Microphone Hotkey");

        private void FeatureTourToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch ts && ViewModel != null)
            {
                ViewModel.ShowFeatureTour = ts.IsOn;
            }
        }
'''

text = text.replace('private async void RecordHotkey_Click(string propertyName, string title)', methods + '\n        private async void RecordHotkey_Click(string propertyName, string title)')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Methods added')
