import sys
import re

path = 'src/SoundBar/Models/AudioAppModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

# Replace AppIcon property
old_app_icon = '''        private Microsoft.UI.Xaml.Media.ImageSource? _appIcon;
        /// <summary>
        /// The visual icon loaded for the UI.
        /// </summary>
        public Microsoft.UI.Xaml.Media.ImageSource? AppIcon
        {
            get => _appIcon;
            private set
            {
                if (_appIcon != value)
                {
                    _appIcon = value;
                    OnPropertyChanged();
                }
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _iconCache = new();'''

new_app_icon = '''        private string? _appIcon;
        /// <summary>
        /// The visual icon loaded for the UI.
        /// </summary>
        public string? AppIcon
        {
            get => _appIcon;
            private set
            {
                if (_appIcon != value)
                {
                    _appIcon = value;
                    OnPropertyChanged();
                }
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _iconCache = new();
        private static readonly string _iconDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SoundBarIcons");'''

text = text.replace(old_app_icon, new_app_icon)

# Replace LoadIconAsync
pattern = re.compile(r'\s*public async Task LoadIconAsync\(\).*?DispatcherQueue\.GetForCurrentThread\(\)\.TryEnqueue\(async \(\) =>[^}]+\}[^}]+\}\);[^}]+\}\s*catch \{ \}\s*\}', re.DOTALL)

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
    print('AudioAppModel updated')
else:
    print('Failed to find LoadIconAsync')

