import sys
import re

path = 'src/SoundBar/Models/AudioAppModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

pattern = re.compile(r'\s*public async Task LoadIconAsync\(\).*?// Access denied or something similar\. No big deal\.\s*\}\s*\}', re.DOTALL)

new_load = '''
        public async Task LoadIconAsync()
        {
            if (string.IsNullOrEmpty(IconPath) || AppIcon != null) return;

            try
            {
                if (_iconCache.TryGetValue(IconPath, out var cachedPath))
                {
                    AppIcon = cachedPath;
                    return;
                }

                // Pop onto a background thread for disk reads.
                string? generatedPath = await Task.Run(() =>
                {
                    try
                    {
                        if (!System.IO.Directory.Exists(_iconDir)) System.IO.Directory.CreateDirectory(_iconDir);
                        
                        using var sha1 = System.Security.Cryptography.SHA1.Create();
                        var hashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(IconPath));
                        var hash = System.BitConverter.ToString(hashBytes).Replace("-", "");
                        string targetPath = System.IO.Path.Combine(_iconDir, hash + ".png");

                        if (System.IO.File.Exists(targetPath))
                        {
                            _iconCache[IconPath] = targetPath;
                            return targetPath;
                        }

                        using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(IconPath);
                        if (sysIcon != null)
                        {
                            using var bmp = sysIcon.ToBitmap();
                            bmp.Save(targetPath, System.Drawing.Imaging.ImageFormat.Png);
                            _iconCache[IconPath] = targetPath;
                            return targetPath;
                        }
                    }
                    catch { }
                    return null;
                });

                if (generatedPath != null)
                {
                    AppIcon = generatedPath;
                }
            }
            catch { }
        }'''

if pattern.search(text):
    text = pattern.sub(new_load, text)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(text)
    print('Replaced')
else:
    print('Not found')
