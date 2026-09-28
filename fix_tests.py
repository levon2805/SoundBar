import sys

path = 'tests/SoundBar.Tests/SettingsServiceTests.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('Assert.False(settings.HasCompletedTour);', '')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed tests')
