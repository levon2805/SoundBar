import sys
import re

path = 'src/SoundBar/ViewModels/MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

# Replace BackgroundImage
old_bg = '''        // Background Image Property
        private Microsoft.UI.Xaml.Media.ImageSource? _backgroundImage;
        public Microsoft.UI.Xaml.Media.ImageSource? BackgroundImage
        {
            get => _backgroundImage;
            private set
            {
                if (_backgroundImage != value)
                {
                    _backgroundImage = value;
                    OnPropertyChanged();
                }
            }
        }'''

new_bg = '''        // Background Image Property
        private string? _backgroundImage;
        public string? BackgroundImage
        {
            get => _backgroundImage;
            private set
            {
                if (_backgroundImage != value)
                {
                    _backgroundImage = value;
                    OnPropertyChanged();
                }
            }
        }'''
text = text.replace(old_bg, new_bg)

# Replace LoadBackgroundImageAsync
pattern = re.compile(r'\s*public async void LoadBackgroundImageAsync\(\)\s*\{\s*try\s*\{\s*string appData = Environment\.GetFolderPath\(Environment\.SpecialFolder\.ApplicationData\);\s*string folder = System\.IO\.Path\.Combine\(appData, "SoundBar", "Backgrounds"\);\s*if \(!System\.IO\.Directory\.Exists\(folder\)\)\s*\{\s*System\.IO\.Directory\.CreateDirectory\(folder\);\s*RunOnUIThread\(\(\) => BackgroundImage = null\);\s*return;\s*\}\s*// Run disk I/O on a background thread.*?else\s*\{\s*RunOnUIThread\(\(\) => BackgroundImage = null\);\s*\}\s*\}\s*catch\s*\{\s*RunOnUIThread\(\(\) => BackgroundImage = null\);\s*\}\s*\}', re.DOTALL)

new_load = '''
        public async void LoadBackgroundImageAsync()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = System.IO.Path.Combine(appData, "SoundBar", "Backgrounds");

                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                    RunOnUIThread(() => BackgroundImage = null);
                    return; 
                }

                // Run disk I/O on a background thread
                string? imagePath = await System.Threading.Tasks.Task.Run(() =>
                {
                    var files = System.IO.Directory.GetFiles(folder);
                    return files.FirstOrDefault(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                                                     f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));
                });

                RunOnUIThread(() => BackgroundImage = imagePath);
            }
            catch
            {
                RunOnUIThread(() => BackgroundImage = null);
            }
        }'''

if pattern.search(text):
    text = pattern.sub(new_load, text)
else:
    print('Failed to replace LoadBackgroundImageAsync')

# Replace CurrentSongThumbnail
old_thumb = '''        private Microsoft.UI.Xaml.Media.Imaging.BitmapImage? _currentSongThumbnail;
        public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? CurrentSongThumbnail
        {
            get => _currentSongThumbnail;
            set
            {
                if (_currentSongThumbnail != value)
                {
                    _currentSongThumbnail = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FallbackIconVisibility));
                }
            }
        }'''

new_thumb = '''        private string? _currentSongThumbnail;
        public string? CurrentSongThumbnail
        {
            get => _currentSongThumbnail;
            set
            {
                if (_currentSongThumbnail != value)
                {
                    _currentSongThumbnail = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FallbackIconVisibility));
                }
            }
        }'''
text = text.replace(old_thumb, new_thumb)

# Replace stream setting
old_stream = '''                    if (e.Thumbnail != null)
                    {
                        try
                        {
                            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                            using var stream = await e.Thumbnail.OpenReadAsync();
                            await bmp.SetSourceAsync(stream);
                            CurrentSongThumbnail = bmp;
                        }
                        catch
                        {
                            CurrentSongThumbnail = null;
                        }
                    }'''

new_stream = '''                    if (e.Thumbnail != null)
                    {
                        try
                        {
                            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SoundBar_Thumbnail.png");
                            using var stream = await e.Thumbnail.OpenReadAsync();
                            using var classicStream = stream.AsStreamForRead();
                            using var fs = System.IO.File.Open(tempPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                            await classicStream.CopyToAsync(fs);
                            CurrentSongThumbnail = tempPath;
                        }
                        catch
                        {
                            CurrentSongThumbnail = null;
                        }
                    }'''
text = text.replace(old_stream, new_stream)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('MainViewModel updated')
