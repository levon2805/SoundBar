import sys

path = 'src/SoundBar/Models/AudioAppModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('OnPropertyChanged(nameof(VolumePercentage));', 'OnPropertyChanged(nameof(VolumePercentage));\n                    OnPropertyChanged(nameof(MuteDisplayText));')
text = text.replace('_isMuted = value;\n                    LastModified = DateTime.Now;\n\n                    OnPropertyChanged();', '_isMuted = value;\n                    LastModified = DateTime.Now;\n\n                    OnPropertyChanged();\n                    OnPropertyChanged(nameof(MuteDisplayText));')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed AudioAppModel')
