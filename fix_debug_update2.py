import sys

path = 'src/SoundBar/Services/UpdateService.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('LatestVersion = "v4.0.1-test";', 'LatestVersion = "v4.0.1-test";\n            UpdateAvailableChanged?.Invoke(this, EventArgs.Empty);')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed Update check for debug 2')
