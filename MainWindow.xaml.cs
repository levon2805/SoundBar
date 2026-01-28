using SoundBar.ViewModels;
using System.Windows;

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
        }
    }
}