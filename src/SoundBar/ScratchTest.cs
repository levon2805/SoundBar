using System;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace SoundBar.Scratch
{
    public class MediaApiTest
    {
        public static async Task TestAsync()
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager.GetCurrentSession();
            if (session != null)
            {
                var timeline = session.GetTimelineProperties();
                Console.WriteLine($"Position: {timeline.Position}");
                Console.WriteLine($"EndTime: {timeline.EndTime}");
                Console.WriteLine($"LastUpdatedTime: {timeline.LastUpdatedTime}");

                // try seek
                var result = await session.TryChangePlaybackPositionAsync(10000000 * 10); // 10 seconds
                Console.WriteLine($"Seek Result: {result}");
            }
        }
    }
}
