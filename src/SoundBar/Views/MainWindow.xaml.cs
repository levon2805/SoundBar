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
        private DispatcherTimer? _focusOutlineTimer;
        private ItemsControl? _appsItemsControl;

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

                // We dynamically build the UI here because the local machine's XAML compiler
                // is throwing MSB4062 and failing to compile new XAML nodes correctly.
                BuildDynamicUI();

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
            ViewModel.ThemeChanged += (s, theme) => ApplyTheme(theme);

            // Start focus outline visual updater
            _focusOutlineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _focusOutlineTimer.Tick += FocusOutlineTimer_Tick;
            _focusOutlineTimer.Start();

            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(null);

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
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

            RestoreWindowPosition();
        }

        private void FocusOutlineTimer_Tick(object? sender, object e)
        {
            if (ViewModel == null) return;

            // Lazily find the ItemsControl because XAML bindings might not be evaluated immediately in the constructor
            if (_appsItemsControl == null)
            {
                var itemsControls = new System.Collections.Generic.List<ItemsControl>();
                FindVisualChildren(this.Content, itemsControls);
                foreach (var ic in itemsControls)
                {
                    if (ic.ItemsSource == ViewModel.Apps)
                    {
                        _appsItemsControl = ic;
                        break;
                    }
                }
            }

            if (_appsItemsControl == null) return;

            foreach (var app in ViewModel.Apps)
            {
                var container = _appsItemsControl.ContainerFromItem(app) as DependencyObject;
                if (container != null)
                {
                    // Find the main Grid inside the DataTemplate
                    var grids = new System.Collections.Generic.List<Grid>();
                    FindVisualChildren(container, grids);
                    if (grids.Count > 0)
                    {
                        var rowGrid = grids[0];
                        if (app.IsFocused && ViewModel.EnableFocusHighlight)
                        {
                            // A clearly visible, translucent blue accent
                            rowGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 0, 120, 215));
                            rowGrid.CornerRadius = new CornerRadius(8);
                        }
                        else
                        {
                            rowGrid.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                        }
                        
                        // Handle Mute Visualization
                        var textBlocks = new System.Collections.Generic.List<TextBlock>();
                        FindVisualChildren(rowGrid, textBlocks);
                        foreach (var tb in textBlocks)
                        {
                            if (Grid.GetColumn(tb) == 3)
                            {
                                if (app.IsMuted)
                                {
                                    tb.Text = "\uE74F"; // Segoe Fluent VolumeMute icon
                                    tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons");
                                    tb.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                                    tb.FontSize = 16;
                                }
                                else
                                {
                                    tb.Text = $"{app.VolumePercentage}%";
                                    tb.ClearValue(TextBlock.FontFamilyProperty);
                                    tb.ClearValue(TextBlock.ForegroundProperty);
                                    tb.ClearValue(TextBlock.FontSizeProperty);
                                }
                            }
                        }

                        // Attach Mute/Unmute click handler to App Icon
                        var images = new System.Collections.Generic.List<Image>();
                        FindVisualChildren(rowGrid, images);
                        if (images.Count > 0)
                        {
                            var img = images[0];
                            // Always detach first to ensure we don't double-subscribe or subscribe to the wrong app model if containers are recycled
                            img.Tapped -= AppIcon_Tapped;
                            img.Tag = app;
                            img.Tapped += AppIcon_Tapped;
                            ToolTipService.SetToolTip(img, "Click to mute/unmute");
                        }
                    }
                }
            }
        }

        private void AppIcon_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is Image img && img.Tag is AudioAppModel app)
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

        private void CompanionButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsCompanionViewMode = !ViewModel.IsCompanionViewMode;
        }

        private void CompanionPowerOff_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.EnableCompanionServer = false;
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

        private void VersionHyperlink_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenReleaseNotes();
        }

        private void BuildDynamicUI()
        {
            try
            {
                // Traverse the visual tree to find all ScrollViewers
                var scrollViewers = new System.Collections.Generic.List<ScrollViewer>();
                FindVisualChildren(this.Content, scrollViewers);
                
                // The settings ScrollViewer is the one containing a StackPanel
                StackPanel? settingsPanel = null;
                
                foreach (var sv in scrollViewers)
                {
                    if (sv.Content is StackPanel sp)
                    {
                        settingsPanel = sp;
                        break;
                    }
                }

                // Also find the ItemsControl that holds our Audio Apps
                var itemsControls = new System.Collections.Generic.List<ItemsControl>();
                FindVisualChildren(this.Content, itemsControls);
                foreach (var ic in itemsControls)
                {
                    if (ic.ItemsSource == ViewModel.Apps)
                    {
                        _appsItemsControl = ic;
                        break;
                    }
                }

                if (settingsPanel == null) return;

                // 1. About & Updates Expander (Top)
                var aboutExpander = new Expander
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Header = new TextBlock { Text = "About & Updates", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                };
                
                var aboutStack = new StackPanel { Spacing = 15 };
                aboutStack.Children.Add(new TextBlock { Text = "Check out the latest features and changes in this version.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
                var releaseNotesBtn = new Button { Content = "View Release Notes" };
                releaseNotesBtn.Click += VersionHyperlink_Click;
                aboutStack.Children.Add(releaseNotesBtn);
                aboutExpander.Content = aboutStack;
                
                // 2. Global Hotkeys Expander
                var keybindsExpander = new Expander
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Header = new TextBlock { Text = "Global Hotkeys", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                };
                
                var keybindsStack = new StackPanel { Spacing = 15 };
                keybindsStack.Children.Add(new TextBlock { Text = "Control the volume of the app you are currently using without leaving it.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
                
                // Volume Up
                var volUpStack = new StackPanel();
                volUpStack.Children.Add(new TextBlock { Text = "Volume Up Hotkey", Margin = new Thickness(0, 0, 0, 5) });
                var volUpBox = new TextBox { PlaceholderText = "e.g. Control+Alt+Up" };
                volUpBox.SetBinding(TextBox.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("VolumeUpHotkey"), Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay });
                volUpStack.Children.Add(volUpBox);
                keybindsStack.Children.Add(volUpStack);
                
                // Volume Down
                var volDownStack = new StackPanel();
                volDownStack.Children.Add(new TextBlock { Text = "Volume Down Hotkey", Margin = new Thickness(0, 0, 0, 5) });
                var volDownBox = new TextBox { PlaceholderText = "e.g. Control+Alt+Down" };
                volDownBox.SetBinding(TextBox.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("VolumeDownHotkey"), Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay });
                volDownStack.Children.Add(volDownBox);
                keybindsStack.Children.Add(volDownStack);
                
                // Mute
                var muteStack = new StackPanel();
                muteStack.Children.Add(new TextBlock { Text = "Mute Hotkey", Margin = new Thickness(0, 0, 0, 5) });
                var muteBox = new TextBox { PlaceholderText = "e.g. Control+Alt+M" };
                muteBox.SetBinding(TextBox.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("MuteHotkey"), Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay });
                muteStack.Children.Add(muteBox);
                keybindsStack.Children.Add(muteStack);
                
                keybindsExpander.Content = keybindsStack;
                
                // 3. Do Not Disturb Expander
                var dndExpander = new Expander
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Header = new TextBlock { Text = "Do Not Disturb Mode", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                };
                
                var dndStack = new StackPanel { Spacing = 15 };
                dndStack.Children.Add(new TextBlock { Text = "Mutes all system sounds and notifications when enabled.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
                var dndToggle = new ToggleSwitch { OnContent = "Enabled", OffContent = "Disabled" };
                dndToggle.SetBinding(ToggleSwitch.IsOnProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("IsDoNotDisturbEnabled"), Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay });
                
                // Keep the icon color logic synced if they toggle from settings menu
                dndToggle.Toggled += DndToggleButton_Changed;
                
                dndStack.Children.Add(dndToggle);
                dndExpander.Content = dndStack;
                
                // Collect existing Expanders
                var expanders = new System.Collections.Generic.Dictionary<string, Expander>();
                foreach (var child in settingsPanel.Children)
                {
                    if (child is Expander exp && exp.Header is TextBlock header)
                    {
                        expanders[header.Text] = exp;
                    }
                }
                
                // Clear the panel to rebuild it in order
                settingsPanel.Children.Clear();
                
                // Helper to add group headers
                void AddCategoryHeader(string title, bool isFirst = false)
                {
                    settingsPanel.Children.Add(new TextBlock 
                    { 
                        Text = title, 
                        FontSize = 16, 
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold, 
                        Margin = new Thickness(0, isFirst ? 0 : 20, 0, 5) 
                    });
                }

                // Helper to add expander if it exists
                void AddExpander(string key, Expander? explicitExpander = null)
                {
                    if (explicitExpander != null)
                        settingsPanel.Children.Add(explicitExpander);
                    else if (expanders.TryGetValue(key, out var exp))
                        settingsPanel.Children.Add(exp);
                }

                // --- Build New Ordered UI ---

                AddCategoryHeader("Personalisation", true);
                AddExpander("Appearance");

                // Dynamically inject the Active App Highlight toggle into the Appearance expander
                // (Done in C# to bypass the MSB4062 XAML compiler error on the host machine)
                if (expanders.TryGetValue("Appearance", out var appearanceExp) && appearanceExp.Content is StackPanel appearanceStack)
                {
                    // Check if it already has it to prevent duplicates on hot reloads
                    bool hasHighlightSetting = false;
                    foreach (var child in appearanceStack.Children)
                    {
                        if (child is ToggleSwitch ts && ts.Header?.ToString() == "Active App Highlight")
                            hasHighlightSetting = true;
                    }

                    if (!hasHighlightSetting)
                    {
                        appearanceStack.Children.Add(new TextBlock 
                        { 
                            Text = "Highlights the active application so you know which volume you are controlling via hotkeys.", 
                            FontSize = 12, 
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 20, 0, 10) 
                        });
                        
                        var highlightToggle = new ToggleSwitch { Header = "Active App Highlight" };
                        highlightToggle.SetBinding(ToggleSwitch.IsOnProperty, new Microsoft.UI.Xaml.Data.Binding 
                        { 
                            Path = new PropertyPath("EnableFocusHighlight"), 
                            Mode = Microsoft.UI.Xaml.Data.BindingMode.TwoWay 
                        });
                        appearanceStack.Children.Add(highlightToggle);
                    }
                }
                AddExpander("Custom Background");

                AddCategoryHeader("Audio & Focus");
                AddExpander("Global Hotkeys", keybindsExpander);
                AddExpander("Do Not Disturb Mode", dndExpander);
                AddExpander("Hearing Protection");
                AddExpander("Media Controls");

                AddCategoryHeader("App Management");
                AddExpander("Hidden Apps");
                AddExpander("Background Apps");

                AddCategoryHeader("General");
                AddExpander("System Integration");
                AddExpander("About & Updates", aboutExpander);

                // Companion button moved to XAML
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to build dynamic UI: {ex.Message}");
            }
        }

        private void FindVisualChildren<T>(DependencyObject parent, System.Collections.Generic.List<T> results) where T : DependencyObject
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                {
                    results.Add(t);
                }
                FindVisualChildren(child, results);
            }
        }

    }
}