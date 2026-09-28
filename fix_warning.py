import sys

path = 'src/SoundBar/ViewModels/MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('string oldThumb = CurrentSongThumbnail;', 'string? oldThumb = CurrentSongThumbnail;')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed warning')
