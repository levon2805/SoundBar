import sys

path = 'src/SoundBar/Models/AudioAppModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_prop = '''        private bool _isFocused;
        /// <summary>
        /// True if this application is currently the active foreground window.
        /// </summary>
        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                if (_isFocused != value)
                {
                    _isFocused = value;
                    OnPropertyChanged();
                }
            }
        }'''

new_prop = '''        private bool _isFocused;
        /// <summary>
        /// True if this application is currently the active foreground window.
        /// </summary>
        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                if (_isFocused != value)
                {
                    _isFocused = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isHighlightVisible;
        /// <summary>
        /// True if this app is focused AND the user has the highlight setting enabled.
        /// </summary>
        public bool IsHighlightVisible
        {
            get => _isHighlightVisible;
            set
            {
                if (_isHighlightVisible != value)
                {
                    _isHighlightVisible = value;
                    OnPropertyChanged();
                }
            }
        }'''

text = text.replace(old_prop, new_prop)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Added IsHighlightVisible')
