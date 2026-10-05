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
        private IntPtr _hWnd;

        /// <summary>
        /// What the user wants the pin to be. We track this ourselves rather than asking Windows,
        /// because Windows can briefly disagree during startup.
        /// </summary>
        private bool _isPinned;
/// <summary>
        /// Sets up the window, wires up the ViewModel, and restores our saved settings.
        /// </summary>
        public MainWindow()
        {
            try
            {
                this.InitializeComponent();

                _settingsService = new SettingsService();
                ViewModel = new MainViewModel(_settingsService);
                ((FrameworkElement)this.Content).DataContext = ViewModel;

                // Hide the top-bar DND button since we moved it into settings
                DndToggleButton.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            }
            catch (Exception ex)
            {
#if DEBUG
                var localFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                System.IO.File.WriteAllText(System.IO.Path.Combine(localFolder, "soundbar_crash_log.txt"), ex.ToString());
#endif
                throw;
            }

            // Apply initial theme and listen for changes
            ApplyTheme(ViewModel.SelectedTheme);
            ViewModel.ThemeChanged += ViewModel_ThemeChanged;

            // Handle layout changes for I/O strip
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateIODeviceLayout(); // Initial setup

            // Title bar is configured in RestoreWindowPosition()

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            _hWnd = hWnd;
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(wndId);
            
            this.Title = "SoundBar";
            _appWindow.Title = "SoundBar";

            // We do NOT use _appWindow.SetIcon() here. 
            // WinUI 3 has a bug where setting the icon dynamically forces a UWP "plate" (white square) behind the taskbar icon.
            // By doing nothing, the OS natively pulls the transparent SoundBar.ico directly from the compiled .exe without any plates!

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = true;
                presenter.SetBorderAndTitleBar(true, false);
            }

            this.Activated += MainWindow_Activated;
                        if (AppTitleText != null)
            {
                AppTitleText.Visibility = ViewModel.UpdateBannerVisibility == Microsoft.UI.Xaml.Visibility.Visible ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
            }
            RestoreWindowPosition();
            ApplyWindowOpacity(ViewModel.WindowOpacity);
        }

        private bool _hasAppliedInitialPin = false;

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (!_hasAppliedInitialPin)
            {
                _hasAppliedInitialPin = true;
                if (_isPinned)
                {
                    // Apply now, and again once the window has finished showing. Windows can ignore
                    // a topmost request made too early, so the second call makes sure it sticks.
                    SetTopmost(true);
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        if (_isPinned) SetTopmost(true);
                    });
                }
            }
        }


        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ShowOutputDevice) || 
                e.PropertyName == nameof(MainViewModel.ShowInputDevice))
            {
                UpdateIODeviceLayout();
            }
            if (e.PropertyName == nameof(MainViewModel.WindowOpacity))
            {
                ApplyWindowOpacity(ViewModel.WindowOpacity);
            }
            if (e.PropertyName == nameof(MainViewModel.UpdateBannerVisibility))
            {
                if (AppTitleText != null)
                {
                    AppTitleText.Visibility = ViewModel.UpdateBannerVisibility == Microsoft.UI.Xaml.Visibility.Visible ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
                }
            }
        }

        private void UpdateIODeviceLayout()
        {
            if (ViewModel == null) return;

            if (ViewModel.ShowOutputDevice && ViewModel.ShowInputDevice)
            {
                OutputColumnDef.Width = new GridLength(1, GridUnitType.Star);
                MuteButtonColumnDef.Width = GridLength.Auto;
                InputColumnDef.Width = new GridLength(1, GridUnitType.Star);
                DeviceComboBox.Margin = new Thickness(0, 0, 6, 0);
                MicMuteButton.Margin = new Thickness(0, 0, 6, 0);
            }
            else if (ViewModel.ShowOutputDevice && !ViewModel.ShowInputDevice)
            {
                OutputColumnDef.Width = new GridLength(1, GridUnitType.Star);
                MuteButtonColumnDef.Width = GridLength.Auto;
                InputColumnDef.Width = GridLength.Auto;
                DeviceComboBox.Margin = new Thickness(0);
            }
            else if (!ViewModel.ShowOutputDevice && ViewModel.ShowInputDevice)
            {
                OutputColumnDef.Width = GridLength.Auto;
                MuteButtonColumnDef.Width = GridLength.Auto;
                InputColumnDef.Width = new GridLength(1, GridUnitType.Star);
                MicMuteButton.Margin = new Thickness(0, 0, 6, 0);
            }
            else
            {
                OutputColumnDef.Width = GridLength.Auto;
                MuteButtonColumnDef.Width = GridLength.Auto;
                InputColumnDef.Width = GridLength.Auto;
            }
        }

        
        private void RecordVolumeUpHotkey_Click(object sender, RoutedEventArgs e) => RecordHotkey_Click("VolumeUpHotkey", "Edit Volume Up Hotkey");
        private void RecordVolumeDownHotkey_Click(object sender, RoutedEventArgs e) => RecordHotkey_Click("VolumeDownHotkey", "Edit Volume Down Hotkey");
        private void RecordMuteHotkey_Click(object sender, RoutedEventArgs e) => RecordHotkey_Click("MuteHotkey", "Edit Mute Hotkey");
        private void RecordInputMuteHotkey_Click(object sender, RoutedEventArgs e) => RecordHotkey_Click("InputMuteHotkey", "Edit Mute Microphone Hotkey");

        private async void RecordHotkey_Click(string propertyName, string title)
        {
            if (ViewModel == null) return;
            
            ViewModel.IsRecordingHotkey = true;

            var dialog = new ContentDialog
            {
                Title = title,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };

            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(new TextBlock { Text = "Press any key combination now..." });
            
            var recordedText = new TextBlock 
            { 
                FontSize = 24, 
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            };
            recordedText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding 
            { 
                Path = new PropertyPath("RecordedHotkeyString"),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay 
            });
            
            stack.Children.Add(recordedText);
            dialog.Content = stack;
            dialog.DataContext = ViewModel;

            var result = await dialog.ShowAsync();

            ViewModel.IsRecordingHotkey = false;

            if (result == ContentDialogResult.Primary)
            {
                if (ViewModel.RecordedHotkeyString != "Listening..." && !string.IsNullOrWhiteSpace(ViewModel.RecordedHotkeyString))
                {
                    ViewModel.SetHotkey(propertyName, ViewModel.RecordedHotkeyString);
                }
            }
        }

        private void AppIcon_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is AudioAppModel app)
            {
                app.IsMuted = !app.IsMuted;
                e.Handled = true;
            }
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
            
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(TitleBarGrid);

            this.Closed += MainWindow_Closed;

            SongPositionSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(SongPositionSlider_PointerPressed), true);

            CreateDesktopShortcut();
        }

        private void ViewModel_ThemeChanged(object? sender, AppTheme theme)
        {
            ApplyTheme(theme);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            SaveWindowSettings();

            SongPositionSlider.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(SongPositionSlider_PointerPressed));

            if (ViewModel != null)
            {
                ViewModel.ThemeChanged -= ViewModel_ThemeChanged;
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Dispose();
            }
        }

        // Makes a desktop shortcut the first time SoundBar runs. If the user deletes it, we respect that.
        private void CreateDesktopShortcut()
        {
            if (_settingsService.Settings.HasCreatedDesktopShortcut) return;

            _settingsService.Settings.HasCreatedDesktopShortcut = true;
            _settingsService.SaveSettings();

            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnkPath = System.IO.Path.Combine(desktopPath, "SoundBar.lnk");

                if (!System.IO.File.Exists(lnkPath))
                {
                    string currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? AppDomain.CurrentDomain.BaseDirectory;
                    string currentAppDir = System.IO.Path.GetDirectoryName(currentExePath) ?? AppDomain.CurrentDomain.BaseDirectory;

                    // Single quotes in paths (e.g. C:\Users\O'Brien) must be doubled for PowerShell
                    static string Ps(string value) => value.Replace("'", "''");

                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"$wshell = New-Object -ComObject WScript.Shell; $s = $wshell.CreateShortcut('{Ps(lnkPath)}'); $s.TargetPath = '{Ps(currentExePath)}'; $s.WorkingDirectory = '{Ps(currentAppDir)}'; $s.Save()\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var shortcutProcess = System.Diagnostics.Process.Start(processInfo);
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
            var settings = _settingsService.Settings;
            var defaults = new AppSettings();

            // Fall back to defaults if the saved spot is the "minimised" parking position or nonsense
            bool validPosition = !WindowPlacementHelper.IsMinimisedPosition(settings.WindowLeft, settings.WindowTop);
            bool validSize = settings.WindowWidth > 0 && settings.WindowHeight > 0;

            var saved = new RectInt32(
                (int)(validPosition ? settings.WindowLeft : defaults.WindowLeft),
                (int)(validPosition ? settings.WindowTop : defaults.WindowTop),
                validSize ? settings.WindowWidth : defaults.WindowWidth,
                validSize ? settings.WindowHeight : defaults.WindowHeight);

            // Make sure it lands fully on a monitor that's actually connected right now
            var workArea = DisplayArea.GetFromRect(saved, DisplayAreaFallback.Nearest).WorkArea;
            _appWindow.MoveAndResize(WindowPlacementHelper.ClampToWorkArea(saved, workArea));

            // The pin itself is applied once the window is shown (see MainWindow_Activated)
            _isPinned = settings.IsPinned;
            UpdatePinButtonVisual(_isPinned);
        }

        // Saves current window state before closing
        private void SaveWindowSettings()
        {
            // While minimised, Windows reports a bogus off-screen position, so keep the last good one
            bool isMinimised = _appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };

            if (!isMinimised)
            {
                var position = _appWindow.Position;
                var size = _appWindow.Size;

                // Update the existing settings object so we don't erase HiddenApps/BackgroundApps
                _settingsService.Settings.WindowTop = position.Y;
                _settingsService.Settings.WindowLeft = position.X;
                _settingsService.Settings.WindowWidth = size.Width;
                _settingsService.Settings.WindowHeight = size.Height;
            }

            _settingsService.Settings.IsPinned = _isPinned;

            _settingsService.SaveSettings();
        }

        // Updates the pin icon visual based on state
        private void UpdatePinButtonVisual(bool isPinned)
        {
            if (PinButton != null)
            {
                PinButton.Opacity = isPinned ? 1.0 : 0.4;
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
                    DndToggleButton.Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorDisabledBrush"];
                }
            }
        }

        

        private void RefreshApps_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ForceRefreshAudioSessions();
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

        private async void UpdateBanner_Click(object sender, RoutedEventArgs e)
        {
            var stack = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 15, Padding = new Microsoft.UI.Xaml.Thickness(0, 10, 0, 0) };
            stack.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Downloading and installing the latest version. SoundBar will restart automatically in a few moments...", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
            stack.Children.Add(new Microsoft.UI.Xaml.Controls.ProgressBar { IsIndeterminate = true });

            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = "Updating SoundBar...",
                Content = stack,
                XamlRoot = this.Content.XamlRoot
            };
            
            _ = dialog.ShowAsync();

            // On success the app closes and restarts, so we only get past this if something went wrong
            string? error = await ViewModel.ApplyUpdate();
            dialog.Hide();

            if (error != null)
            {
                var errorDialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    Title = "Update didn't work",
                    Content = new Microsoft.UI.Xaml.Controls.TextBlock { Text = error, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private void DismissLoudnessWarning_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.DismissLoudnessWarning();
        }

        private void OpenSystemSounds_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenSystemSounds();
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
            _isPinned = !_isPinned;
            SetTopmost(_isPinned);
            UpdatePinButtonVisual(_isPinned);
        }

        private void CompanionButton_Click(object sender, RoutedEventArgs e)
        {
            // The companion screen lives in the main view, so leave Settings first or nothing would appear
            if (SettingsContentGrid.Visibility == Visibility.Visible)
            {
                CloseSettingsButton_Click(sender, e);
                ViewModel.IsCompanionViewMode = true;
                return;
            }

            ViewModel.IsCompanionViewMode = !ViewModel.IsCompanionViewMode;
        }

        private void CloseCompanionButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsCompanionViewMode = false;
        }

        private void ResetOpacity_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetWindowOpacity();
        }

        /// <summary>
        /// Makes the whole window see-through. At 100% we switch transparency off entirely,
        /// so people who never touch the slider get exactly the same window as before.
        /// </summary>
        private void ApplyWindowOpacity(double percent)
        {
            if (_hWnd == IntPtr.Zero) return;

            long exStyle = NativeMethods.GetWindowLongPtr(_hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();

            if (percent >= 100)
            {
                if ((exStyle & NativeMethods.WS_EX_LAYERED) != 0)
                {
                    NativeMethods.SetWindowLongPtr(_hWnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle & ~(long)NativeMethods.WS_EX_LAYERED));
                }
                return;
            }

            if ((exStyle & NativeMethods.WS_EX_LAYERED) == 0)
            {
                NativeMethods.SetWindowLongPtr(_hWnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle | NativeMethods.WS_EX_LAYERED));
            }

            byte alpha = (byte)Math.Round(Math.Clamp(percent, AppSettings.MinWindowOpacity, 100) / 100.0 * 255);
            NativeMethods.SetLayeredWindowAttributes(_hWnd, 0, alpha, NativeMethods.LWA_ALPHA);
        }

        private void MicMuteButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ToggleInputMute();
        }

        private void CompanionPowerOn_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EnableCompanionServer = true;
        }

        private void CompanionPowerOff_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EnableCompanionServer = false;
        }

        private void SetTopmost(bool topmost)
        {
            // Keep WinUI's own flag in sync...
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = topmost;
            }

            // ...but also tell Windows directly. If WinUI's flag already says "true" it treats
            // setting it again as a no-op, which is what left the pin looking on but not working.
            if (_hWnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(_hWnd,
                    topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
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

        private void VersionHyperlink_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenReleaseNotes();
        }
    }
}
