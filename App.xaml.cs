using Microsoft.UI.Xaml;

namespace SoundBar
{
    public partial class App : Application
    {
        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            m_window = new SoundBar.Views.MainWindow();
            m_window.Activate();
        }

        private Window? m_window;
    }
}