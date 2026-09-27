import sys

path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<Image Stretch="UniformToFill" Source="{x:Bind ViewModel.BackgroundImage, Mode=OneWay}" />', '''<Image Stretch="UniformToFill">
                <Image.Source>
                    <BitmapImage UriSource="{x:Bind ViewModel.BackgroundImage, Mode=OneWay}" DecodePixelWidth="800" />
                </Image.Source>
            </Image>''')

text = text.replace('<Image Source="{x:Bind ViewModel.CurrentSongThumbnail, Mode=OneWay}" Stretch="UniformToFill">', '''<Image Stretch="UniformToFill">
                                    <Image.Source>
                                        <BitmapImage UriSource="{x:Bind ViewModel.CurrentSongThumbnail, Mode=OneWay}" DecodePixelWidth="250" />
                                    </Image.Source>''')

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)
print('XAML updated')
