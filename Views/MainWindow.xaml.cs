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
            
            _appWindow.Resize(new SizeInt32(400, 500));

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
                presenter.SetBorderAndTitleBar(true, false);
            }

            LoadWindowSettings();
            
            TitleBarGrid.PointerPressed += TitleBarGrid_PointerPressed;
            TitleBarGrid.PointerMoved += TitleBarGrid_PointerMoved;
            TitleBarGrid.PointerReleased += TitleBarGrid_PointerReleased;
            TitleBarGrid.PointerCanceled += TitleBarGrid_PointerCanceled;
        }

        // Loads saved settings like window position and pinned state
        private void LoadWindowSettings()
        {
            var settings = _settingsService.Load();

            _appWindow.Move(new PointInt32((int)settings.WindowLeft, (int)settings.WindowTop));
            
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
            var isPinned = IsTopmost();

            var settings = new AppSettings
            {
                WindowTop = position.Y,
                WindowLeft = position.X,
                IsPinned = isPinned
            };

            _settingsService.Save(settings);
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