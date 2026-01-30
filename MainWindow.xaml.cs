using SoundBar.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace SoundBar
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Attaching logic to the UI
            this.DataContext = new MainViewModel();

            // Enable dragging window by clicking anywhere
            this.MouseDown += Window_MouseDown;
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
            this.Close();
        }
    }
}