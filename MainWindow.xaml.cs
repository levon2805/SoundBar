using System.Threading;
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

            // Create the thread
            var audioThread = new Thread(() =>
            {
                try
                {
                    // Create the service
                    var audioService = new SoundBar.Services.WindowsAudioMixerService();

                    // Ask for list of apps
                    var currentApps = audioService.GetActiveAudioSessions();

                    // Print to debug output
                    foreach (var app in currentApps)
                    {
                        System.Diagnostics.Debug.WriteLine($"App Found: {app.Name} | Volume: {app.Volume}");
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Crashed: {ex.Message}");
                }
            });

            // Force thread to be MTA
            audioThread.SetApartmentState(ApartmentState.MTA);

            // Start the thread
            audioThread.Start();
        }
    }
}