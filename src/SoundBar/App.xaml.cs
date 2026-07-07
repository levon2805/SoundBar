using Microsoft.UI.Xaml;

namespace SoundBar
{
    /// <summary>
    /// Provides application-specific behaviour to supplement the default Application class.
    /// This is where the magic begins.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched by the end user.
        /// We just spin up the main window and bring it to the front.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            m_window = new SoundBar.Views.MainWindow();
            m_window.Activate();
        }

        private Window? m_window;
    }
}