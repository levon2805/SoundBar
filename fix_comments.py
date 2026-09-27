import sys

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<!-- --- Appearance & Layout --- -->', '<!-- Appearance & Layout Header -->')
text = text.replace('<!-- --- Audio & Focus --- -->', '<!-- Audio & Focus Header -->')
text = text.replace('<!-- --- App Management --- -->', '<!-- App Management Header -->')
text = text.replace('<!-- --- General --- -->', '<!-- General Header -->')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed Comments')
