import sys

path = 'src/SoundBar/Services/WindowsAudioMixerService.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('''        private readonly HashSet<string> _addedNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<int> _seenProcessIdsThisTick = new();''', '')

old_clear = '''            var sessions = new List<AudioSessionData>();

            _addedNames.Clear();
            _seenProcessIdsThisTick.Clear();'''

new_clear = '''            var sessions = new List<AudioSessionData>();

            var _addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var _seenProcessIdsThisTick = new HashSet<int>();'''

text = text.replace(old_clear, new_clear)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed C5')
