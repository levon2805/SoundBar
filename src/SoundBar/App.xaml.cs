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
            // Check if we are the main instance
            var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("SoundBarApp");
            
            if (!mainInstance.IsCurrent)
            {
                // We are a secondary instance! Send our launch args to the main instance
                var currentArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                mainInstance.RedirectActivationToAsync(currentArgs).AsTask().GetAwaiter().GetResult();
                
                // Terminate this duplicate instance
                System.Environment.Exit(0);
                return;
            }

            // We are the main instance. Listen for future duplicate launches so we can pop up
            mainInstance.Activated += MainInstance_Activated;

            m_window = new SoundBar.Views.MainWindow();
            m_window.Activate();
        }

        private void MainInstance_Activated(object? sender, Microsoft.Windows.AppLifecycle.AppActivationArguments e)
        {
            // A duplicate instance tried to launch. Bring our existing window to the front.
            if (m_window != null)
            {
                m_window.DispatcherQueue.TryEnqueue(() =>
                {
                    m_window.Activate();
                });
            }
        }

        private Window? m_window;
    }
}