using SoundBar.Models;
using SoundBar.Services;
using SoundBar.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoundBar
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SettingsService _settingsService;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize services
            _settingsService = new SettingsService();

            // Attaching logic to the UI
            this.DataContext = new MainViewModel();

            // Load and apply saved settings (Position & Pin)
            LoadWindowSettings();

            // Enable dragging window by clicking anywhere
            this.MouseDown += Window_MouseDown;
        }

        private void LoadWindowSettings()
        {
            var settings = _settingsService.Load();

            this.Top = settings.WindowTop;
            this.Left = settings.WindowLeft;
            this.Topmost = settings.IsPinned;

            // Update visual style of the button based on loaded settings
            UpdatePinButtonVisual();
        }

        private void SaveWindowSettings()
        {
            var settings = new AppSettings
            {
                WindowTop = this.Top,
                WindowLeft = this.Left,
                IsPinned = this.Topmost
            };

            _settingsService.Save(settings);
        }

        private void UpdatePinButtonVisual()
        {
            // Only update if the button has been initialized
            if (PinButton != null)
            {
                if (this.Topmost)
                {
                    // Bright White for active
                    PinButton.Foreground = Brushes.White;
                }
                else
                {
                    // Dim Grey for inactive
                    PinButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
                }
            }
        }

        // Logic to move the window when clicked and dragged
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        // Logic for the new "X" close button
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Save settings before closing
            SaveWindowSettings();
            this.Close();
        }

        // Logic for the "Always on Top" pin
        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the Window property
            this.Topmost = !this.Topmost;

            // Change visual style of the button
            UpdatePinButtonVisual();
        }
    }
}