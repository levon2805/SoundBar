using SoundBar.Models;
using SoundBar.Services;
using SoundBar.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

using SoundBar.Helpers;

namespace SoundBar.Views
{
    /// <summary>
    /// The main window of our application where all the action happens.
    /// It handles all the UI interactions, dragging, and passing commands down to the ViewModel.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        /// <summary>
        /// Our connection to the brains of the operation.
        /// </summary>
        public MainViewModel ViewModel { get; }

        private readonly SettingsService _settingsService;
        private AppWindow _appWindow;

        /// <summary>
        /// Sets up the window, wires up the ViewModel, and restores our saved settings.
        /// </summary>
        public MainWindow()
        {
            this.InitializeComponent();

            _settingsService = new SettingsService();
            ViewModel = new MainViewModel(_settingsService);
            ((FrameworkElement)this.Content).DataContext = ViewModel;

            // Apply initial theme and listen for changes
            ApplyTheme(ViewModel.SelectedTheme);
            ViewModel.ThemeChanged += (s, theme) => ApplyTheme(theme);

            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(null);

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(wndId);
            
            this.Title = "SoundBar";
            _appWindow.Title = "SoundBar";

            // Set the icon for the taskbar and window
            string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SoundBar.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = true;
                presenter.SetBorderAndTitleBar(true, false);
            }

            RestoreWindowPosition();
        }

        private void ApplyTheme(AppTheme theme)
        {
            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = theme switch
                {
                    AppTheme.Light => ElementTheme.Light,
                    AppTheme.Dark => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                };
            }
        }

        private void RestoreWindowPosition()
        {
            LoadWindowSettings();
            
            TitleBarGrid.PointerPressed += TitleBarGrid_PointerPressed;
            TitleBarGrid.PointerMoved += TitleBarGrid_PointerMoved;
            TitleBarGrid.PointerReleased += TitleBarGrid_PointerReleased;
            TitleBarGrid.PointerCanceled += TitleBarGrid_PointerCanceled;

            this.Closed += (s, e) => { ViewModel.Dispose(); };

            SongPositionSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(SongPositionSlider_PointerPressed), true);

            CreateDesktopShortcut();
        }

        private void CreateDesktopShortcut()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnkPath = System.IO.Path.Combine(desktopPath, "SoundBar.lnk");

                if (!System.IO.File.Exists(lnkPath))
                {
                    string currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? AppDomain.CurrentDomain.BaseDirectory;
                    string currentAppDir = System.IO.Path.GetDirectoryName(currentExePath) ?? AppDomain.CurrentDomain.BaseDirectory;

                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"$wshell = New-Object -ComObject WScript.Shell; $s = $wshell.CreateShortcut('{lnkPath}'); $s.TargetPath = '{currentExePath}'; $s.WorkingDirectory = '{currentAppDir}'; $s.Save()\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    System.Diagnostics.Process.Start(processInfo);
                }
            }
            catch
            {
                // Ignore if it fails
            }
        }

        // Loads saved settings like window position and pinned state
        private void LoadWindowSettings()
        {
            var settings = _settingsService.Load();

            _appWindow.MoveAndResize(new RectInt32(
                (int)settings.WindowLeft, 
                (int)settings.WindowTop, 
                settings.WindowWidth, 
                settings.WindowHeight));
            
            if (settings.IsPinned)
            {
                SetTopmost(true);
            }

            UpdatePinButtonVisual(settings.IsPinned);
        }

        // Saves current window state before closing
        private void SaveWindowSettings()
        {
            var position = _appWindow.Position;
            var size = _appWindow.Size;
            var isPinned = IsTopmost();

            // Update the existing settings object so we don't erase HiddenApps/BackgroundApps/Presets
            _settingsService.Settings.WindowTop = position.Y;
            _settingsService.Settings.WindowLeft = position.X;
            _settingsService.Settings.WindowWidth = size.Width;
            _settingsService.Settings.WindowHeight = size.Height;
            _settingsService.Settings.IsPinned = isPinned;

            _settingsService.SaveSettings();
        }

        // Updates the pin icon colour based on state
        private void UpdatePinButtonVisual(bool isPinned)
        {
            if (PinButton != null)
            {
                if (isPinned)
                {
                    PinButton.Foreground = new SolidColorBrush(Colors.White);
                }
                else
                {
                    PinButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 85, 85, 85));
                }
            }
        }

        // Updates the DND icon colour based on state
        private void DndToggleButton_Changed(object sender, RoutedEventArgs e)
        {
            if (DndToggleButton != null)
            {
                if (DndToggleButton.IsChecked == true)
                {
                    DndToggleButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 204, 0)); // Yellow Moon
                }
                else
                {
                    DndToggleButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 85, 85, 85)); // Dim Gray
                }
            }
        }

        // Drag state variables
        private bool _isDragging = false;
        private NativeMethods.POINT _dragStartCursorPos;
        private PointInt32 _dragStartWindowPos;

        // Triggered when clicking the custom title bar area
        private void TitleBarGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;
            if (properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                _isDragging = true;
                NativeMethods.GetCursorPos(out _dragStartCursorPos);
                _dragStartWindowPos = _appWindow.Position;
                ((UIElement)sender).CapturePointer(e.Pointer);
            }
        }

        // Triggered when dragging the title bar
        private void TitleBarGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                NativeMethods.GetCursorPos(out NativeMethods.POINT currentCursorPos);
                int dx = currentCursorPos.X - _dragStartCursorPos.X;
                int dy = currentCursorPos.Y - _dragStartCursorPos.Y;
                _appWindow.Move(new PointInt32(_dragStartWindowPos.X + dx, _dragStartWindowPos.Y + dy));
            }
        }

        // Triggered when releasing the drag
        private void TitleBarGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ((UIElement)sender).ReleasePointerCapture(e.Pointer);
            }
        }

        // Triggered if the drag is canceled by the system
        private void TitleBarGrid_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ((UIElement)sender).ReleasePointerCapture(e.Pointer);
            }
        }

        // Triggered when clicking the Hide button next to an app
        private void HideAppButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is AudioAppModel app)
            {
                ViewModel.HideApp(app.DisplayName ?? string.Empty);
            }
        }

        // Triggered when clicking the Unhide button inside the hidden apps menu
        private void UnhideAppButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is string appName)
            {
                ViewModel.UnhideApp(appName);
            }
        }

        // Triggered when clicking the Allow button inside the background apps menu
        private void AllowBackgroundAppButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is string appName)
            {
                ViewModel.AllowBackgroundApp(appName);
            }
        }

        // Toggle Settings Menu
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentGrid.Visibility = Visibility.Collapsed;
            SettingsContentGrid.Visibility = Visibility.Visible;
            SettingsButton.Visibility = Visibility.Collapsed;
        }

        private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsContentGrid.Visibility = Visibility.Collapsed;
            MainContentGrid.Visibility = Visibility.Visible;
            SettingsButton.Visibility = Visibility.Visible;
        }

        private void OpenBackgroundFolder_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenBackgroundFolder();
        }

        private void ReloadBackground_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ReloadBackground();
        }

        private void UpdateBanner_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ApplyUpdate();
        }

        private void DismissLoudnessWarning_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DismissLoudnessWarning();
        }

        private async void SavePresetButton_Click(object sender, RoutedEventArgs e)
        {
            var textBox = new TextBox { PlaceholderText = "e.g., Gaming Mode, Focus Mode", Margin = new Thickness(0, 10, 0, 0) };
            
            var dialog = new ContentDialog
            {
                Title = "Save New Audio Preset",
                Content = new StackPanel 
                { 
                    Children = 
                    { 
                        new TextBlock { Text = "Enter a name for this preset to save your current active volumes:" },
                        textBox
                    }
                },
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                ViewModel.SavePreset(textBox.Text);
            }
        }

        private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedPreset != null)
            {
                ViewModel.DeletePreset(ViewModel.SelectedPreset);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveWindowSettings();
            this.Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            bool isPinned = !IsTopmost();
            SetTopmost(isPinned);
            UpdatePinButtonVisual(isPinned);
        }

        private void SetTopmost(bool topmost)
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = topmost;
            }
        }

        private bool IsTopmost()
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                return presenter.IsAlwaysOnTop;
            }
            return false;
        }

        private void MediaPrevious_Click(object sender, RoutedEventArgs e)
        {
            MediaHelper.PreviousTrack();
        }

        private void MediaPlayPause_Click(object sender, RoutedEventArgs e)
        {
            MediaHelper.PlayPause();
        }

        private void MediaNext_Click(object sender, RoutedEventArgs e)
        {
            MediaHelper.NextTrack();
        }

        private void MediaMute_Click(object sender, RoutedEventArgs e)
        {
            MediaHelper.Mute();
        }

        private void ToggleMusicPlayerMode_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsMusicPlayerMode = !ViewModel.IsMusicPlayerMode;
        }

        private void SongPositionSlider_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ViewModel.IsUserScrubbing = true;
        }

        private void SongPositionSlider_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            ViewModel.IsUserScrubbing = false;
            ViewModel.SeekToScrubPosition();
        }

        private void TextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                // Force focus back to the main content grid to trigger LostFocus binding update
                MainContentGrid.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }
    }
}