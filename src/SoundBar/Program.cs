using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace SoundBar
{
    /// <summary>
    /// SoundBar's own start-up, in place of the one WinUI generates (see DISABLE_XAML_GENERATED_MAIN in the project).
    /// </summary>
    public static class Program
    {
        /// <summary>The key every copy of SoundBar registers under, so only one runs at a time.</summary>
        internal const string InstanceKey = "SoundBarApp";

        [STAThread]
        private static int Main(string[] args)
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            // Already running? Hand this launch over (so its window comes to the front) and close, before any
            // window or XAML is set up
            if (HandedOverToRunningCopy()) return 0;

            Microsoft.UI.Xaml.Application.Start(start =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
            return 0;
        }

        /// <summary>
        /// If another SoundBar is already running, passes this launch to it and returns true.
        /// </summary>
        /// <remarks>
        /// This follows Microsoft's single-instance sample: hand over from Main, on a background thread. It used to
        /// happen inside the app on the window's thread, waiting there for the answer, which can hang and leave an
        /// invisible SoundBar behind. It's also given a time limit, so this copy always closes.
        /// </remarks>
        private static bool HandedOverToRunningCopy()
        {
            AppInstance main;
            try
            {
                main = AppInstance.FindOrRegisterForKey(InstanceKey);
            }
            catch (Exception ex)
            {
                // Can't tell, so just run
                System.Diagnostics.Debug.WriteLine($"Single-instance check failed: {ex.Message}");
                return false;
            }

            if (main.IsCurrent) return false;

            try
            {
                var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                var handOver = Task.Run(() => main.RedirectActivationToAsync(activation).AsTask());
                handOver.Wait(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                // The other copy will just not come to the front; this one still closes
                System.Diagnostics.Debug.WriteLine($"Couldn't hand over to the running SoundBar: {ex.Message}");
            }
            return true;
        }
    }
}
