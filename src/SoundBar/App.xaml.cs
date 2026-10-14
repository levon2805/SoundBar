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
        /// Sets up the one and only app object.
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.UnhandledException += App_UnhandledException;
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
                    m_window.BringToFront();
                });
            }
        }

        /// <summary>
        /// Last line of defence for errors nobody caught (usually from an async click handler).
        /// We note it down and keep running, because a volume mixer vanishing is worse than one hiccup.
        /// </summary>
        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                string folder = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "SoundBar");
                System.IO.Directory.CreateDirectory(folder);
                string logPath = System.IO.Path.Combine(folder, "error.log");

                // Start afresh once the log gets big, so it can't grow forever
                var log = new System.IO.FileInfo(logPath);
                if (log.Exists && log.Length > 256 * 1024) log.Delete();

                System.IO.File.AppendAllText(logPath, $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] {SoundBar.Services.UpdateService.CurrentVersion}\n{e.Exception}\n\n");
            }
            catch { }

            // If the window never got built there's nothing to keep alive. Letting it crash is better than
            // leaving an invisible process behind that swallows every future launch.
            e.Handled = m_window != null;
        }

        private SoundBar.Views.MainWindow? m_window;
    }
}