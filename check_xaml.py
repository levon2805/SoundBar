import xml.etree.ElementTree as ET

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

# Try parsing as XML to find the line error
try:
    # Need to add dummy namespace since XAML uses xmlns
    ET.fromstring(text)
    print('XML is valid!')
except ET.ParseError as e:
    print(f'Parse Error: {e}')
