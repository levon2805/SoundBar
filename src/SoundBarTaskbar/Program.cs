using SoundBar.Shared;

namespace SoundBarTaskbar
{
    /// <summary>
    /// SoundBar Taskbar: shows the volume and microphone state on the Windows taskbar. It runs on its own (it
    /// has its own startup shortcut), so it works whether or not SoundBar itself is open.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            // Only one copy at a time
            using var mutex = new Mutex(true, TaskbarHelperInfo.MutexName, out bool isFirst);
            if (!isFirst) return 0;

            try
            {
                using var indicator = new IndicatorWindow(HelperSettings.Load());
                return indicator.Run();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
