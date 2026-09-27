import sys

path = 'src/SoundBar/ViewModels/MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

old_block = '''                        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SoundBar_Thumbnail.png");
                        using var stream = await e.Thumbnail.OpenReadAsync();
                        using var classicStream = stream.AsStreamForRead();
                        using var fs = System.IO.File.Open(tempPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                        await classicStream.CopyToAsync(fs);
                        CurrentSongThumbnail = tempPath;'''

new_block = '''                        string oldThumb = CurrentSongThumbnail;
                        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SoundBar_Thumbnail_{Guid.NewGuid():N}.png");
                        using var stream = await e.Thumbnail.OpenReadAsync();
                        using var classicStream = stream.AsStreamForRead();
                        using var fs = System.IO.File.Open(tempPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                        await classicStream.CopyToAsync(fs);
                        CurrentSongThumbnail = tempPath;
                        
                        if (oldThumb != null && System.IO.File.Exists(oldThumb))
                        {
                            try { System.IO.File.Delete(oldThumb); } catch { }
                        }'''

text = text.replace(old_block, new_block)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('Fixed thumbnail logic')
