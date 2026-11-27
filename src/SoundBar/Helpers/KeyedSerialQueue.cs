using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SoundBar.Helpers
{
    /// <summary>
    /// Runs work in the background, one piece at a time per key, in the order it was asked for.
    /// </summary>
    /// <remarks>
    /// Dragging a volume slider sends many changes a few milliseconds apart. Each used to go off on its own
    /// background task, and those can finish in any order, so an older volume could land after a newer one and
    /// leave Windows at a different level from the one the slider shows. Queuing them per app keeps them in order,
    /// while different apps still don't wait for each other.
    /// </remarks>
    public sealed class KeyedSerialQueue
    {
        private readonly Dictionary<string, Task> _tails = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        /// <summary>
        /// Queues <paramref name="work"/> to run after everything already queued for <paramref name="key"/>.
        /// An exception in it is swallowed, so it can't stop later work for the same key.
        /// </summary>
        public Task Enqueue(string key, Action work)
        {
            lock (_lock)
            {
                var previous = _tails.TryGetValue(key, out var tail) ? tail : Task.CompletedTask;
                var next = previous.ContinueWith(_ =>
                {
                    try { work(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Queued work for {key} failed: {ex.Message}"); }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                _tails[key] = next;
                return next;
            }
        }
    }
}
