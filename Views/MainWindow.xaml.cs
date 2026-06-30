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
    public sealed partial class MainWindow : Window
    {
        // View Model accessor
        public MainViewModel ViewModel { get; }

        // Dependencies
        private readonly SettingsService _settingsService;
        private AppWindow _appWindow;

        // Constructor
        public MainWindow()
        {
            this.InitializeComponent();

            _settingsService = new SettingsService();
            ViewModel = new MainViewModel(_settingsService);
            ((FrameworkElement)this.Content).DataContext = ViewModel;

            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(null);

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(wndId);
            
            this.Title = "SoundBar";
            _appWindow.Title = "SoundBar";

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = true;
                presenter.SetBorderAndTitleBar(true, false);
            }

            LoadWindowSettings();
            
            TitleBarGrid.PointerPressed += TitleBarGrid_PointerPressed;
            TitleBarGrid.PointerMoved += TitleBarGrid_PointerMoved;
            TitleBarGrid.PointerReleased += TitleBarGrid_PointerReleased;
            TitleBarGrid.PointerCanceled += TitleBarGrid_PointerCanceled;

            this.Closed += (s, e) => { ViewModel.Dispose(); };

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

        // Updates the pin icon color based on state
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
                ViewModel.HideApp(app.Name ?? string.Empty);
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

        private void UpdateBanner_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ApplyUpdate();
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
    }
}