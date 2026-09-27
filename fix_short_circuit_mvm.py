import sys

path = 'src/SoundBar/ViewModels/MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_block = '''                string newTitle = string.IsNullOrEmpty(e.Title) ? "Not Playing" : e.Title;
                if (CurrentSongTitle == newTitle && CurrentSongArtist == e.Artist && CurrentSongThumbnail != null)
                {
                    return;
                }

                CurrentSongTitle = newTitle;
                CurrentSongArtist = e.Artist;'''

new_block = '''                string newTitle = string.IsNullOrEmpty(e.Title) ? "Not Playing" : e.Title;

                CurrentSongTitle = newTitle;
                CurrentSongArtist = e.Artist;'''

text = text.replace(old_block, new_block)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed MainViewModel')
