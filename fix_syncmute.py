import sys

path = 'src/SoundBar/Models/AudioAppModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('OnPropertyChanged(nameof(IsMuted));', 'OnPropertyChanged(nameof(IsMuted));\n                OnPropertyChanged(nameof(MuteDisplayText));')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed AudioAppModel SyncMute')
