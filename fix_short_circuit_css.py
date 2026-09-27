import sys

path = 'src/SoundBar/Services/CompanionServerService.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_block = '''        private void OnMediaInfoChanged(object? sender, MediaInfoEventArgs e)
        {
            if (_currentTitle == e.Title && _currentArtist == e.Artist)
            {
                // Song hasn't changed, skip heavy base64 encoding
                return;
            }

            _currentTitle = e.Title;
            _currentArtist = e.Artist;'''

new_block = '''        private void OnMediaInfoChanged(object? sender, MediaInfoEventArgs e)
        {
            _currentTitle = e.Title;
            _currentArtist = e.Artist;'''

text = text.replace(old_block, new_block)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed CompanionServerService')
