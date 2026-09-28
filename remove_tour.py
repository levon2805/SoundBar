import sys
import re

# 1. AppSettings.cs
path = 'src/SoundBar/Models/AppSettings.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = re.sub(r'/// <summary>\s*/// Whether the user has completed the guided feature tour.\s*/// </summary>\s*public bool HasCompletedTour \{ get; set; \} = false;\s*', '', text)
text = re.sub(r'/// <summary>\s*/// Whether to show the Feature Tour button in settings.\s*/// </summary>\s*public bool ShowFeatureTour \{ get; set; \} = true;\s*', '', text)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)

# 2. MainViewModel.cs
path = 'src/SoundBar/ViewModels/MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = re.sub(r'/// <summary>\s*/// Whether the user has completed the guided feature tour.\s*/// </summary>\s*public bool HasCompletedTour\s*\{\s*get => _settingsService\.Settings\.HasCompletedTour;\s*set\s*\{\s*_settingsService\.Settings\.HasCompletedTour = value;\s*_settingsService\.SaveSettings\(\);\s*OnPropertyChanged\(\);\s*\}\s*\}\s*', '', text)
text = re.sub(r'/// <summary>\s*/// Whether to show the Feature Tour button in settings.\s*/// </summary>\s*public bool ShowFeatureTour\s*\{\s*get => _settingsService\.Settings\.ShowFeatureTour;\s*set\s*\{\s*_settingsService\.Settings\.ShowFeatureTour = value;\s*_settingsService\.SaveSettings\(\);\s*OnPropertyChanged\(\);\s*\}\s*\}\s*', '', text)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)

# 3. MainWindow.xaml
path = 'src/SoundBar/Views/MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = re.sub(r'<ToggleSwitch Header="Feature Tour Button" OnContent="Visible" OffContent="Hidden" IsOn="\{x:Bind ViewModel\.ShowFeatureTour, Mode=TwoWay\}" Toggled="FeatureTourToggle_Toggled"/>\s*', '', text)

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)

# 4. MainWindow.xaml.cs
path = 'src/SoundBar/Views/MainWindow.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    text = f.read()

text = re.sub(r'private void FeatureTourToggle_Toggled\(object sender, RoutedEventArgs e\)\s*\{\s*if \(sender is ToggleSwitch ts && ViewModel != null\)\s*\{\s*ViewModel\.ShowFeatureTour = ts\.IsOn;\s*\}\s*\}\s*', '', text)

# Now remove the whole tour block from MainWindow.xaml.cs
# It starts at: private Border? _tourOverlay;
# And ends at the end of BuildTourSteps()

match = re.search(r'private Border\? _tourOverlay;', text)
if match:
    start_idx = match.start()
    
    # Find the end of BuildTourSteps()
    end_match = re.search(r'\(CompanionToggleButton, "Mobile Companion",\s*"Launch the companion server and scan the QR code to control your audio from your phone\."\)\s*\};\s*\}\s*', text)
    if end_match:
        end_idx = end_match.end()
        text = text[:start_idx] + text[end_idx:]

with open(path, 'w', encoding='utf-8') as f:
    f.write(text)

print('Removed Tour')
