import sys
import re

path = 'src/SoundBar/ViewModels/MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

pattern = re.compile(r'var bmp = new Microsoft\.UI\.Xaml\.Media\.Imaging\.BitmapImage\(\);\s*using var stream = await e\.Thumbnail\.OpenReadAsync\(\);\s*await bmp\.SetSourceAsync\(stream\);\s*CurrentSongThumbnail = bmp;', re.DOTALL)

new_stream = '''string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SoundBar_Thumbnail.png");
                        using var stream = await e.Thumbnail.OpenReadAsync();
                        using var classicStream = stream.AsStreamForRead();
                        using var fs = System.IO.File.Open(tempPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                        await classicStream.CopyToAsync(fs);
                        CurrentSongThumbnail = tempPath;'''

text = pattern.sub(new_stream, text)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed stream')
