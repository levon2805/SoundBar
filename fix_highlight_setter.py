import sys

path = 'src/SoundBar/ViewModels/MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_setter = '''        public bool EnableFocusHighlight
        {
            get => _settingsService.Settings.EnableFocusHighlight;
            set
            {
                if (_settingsService.Settings.EnableFocusHighlight != value)
                {
                    _settingsService.Settings.EnableFocusHighlight = value;
                    OnPropertyChanged();
                    _settingsService.SaveSettings();
                }
            }
        }'''

new_setter = '''        public bool EnableFocusHighlight
        {
            get => _settingsService.Settings.EnableFocusHighlight;
            set
            {
                if (_settingsService.Settings.EnableFocusHighlight != value)
                {
                    _settingsService.Settings.EnableFocusHighlight = value;
                    OnPropertyChanged();
                    _settingsService.SaveSettings();
                    
                    // Update all apps immediately so the UI reflects the toggle
                    foreach (var app in Apps)
                    {
                        app.IsHighlightVisible = app.IsFocused && value;
                    }
                }
            }
        }'''

text = text.replace(old_setter, new_setter)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed Highlight Setter')
