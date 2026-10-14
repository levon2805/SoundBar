using System;
using System.Collections.Generic;

namespace SoundBar.Services
{
    /// <summary>
    /// Slows down anyone trying to guess the four-digit pairing code. Five wrong codes locks that address out
    /// for 5 minutes, and every lockout after that lasts twice as long as the one before (up to an hour).
    /// </summary>
    internal sealed class PairingAttemptTracker
    {
        public const int MaxAttempts = 5;
        public static readonly TimeSpan FirstLockout = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(60);

        /// <summary>
        /// How long we remember someone's wrong guesses and past lockouts after their last wrong guess.
        /// </summary>
        public static readonly TimeSpan Memory = TimeSpan.FromHours(1);

        private sealed class Record
        {
            public int Attempts;
            public int Lockouts;
            public DateTime LockoutEnd;
            public DateTime LastFailure;
        }

        private readonly Dictionary<string, Record> _records = new();
        private readonly object _lock = new();

        /// <summary>
        /// True if this address is locked out right now, along with how long they've got left.
        /// </summary>
        public bool IsLockedOut(string address, DateTime now, out TimeSpan remaining)
        {
            lock (_lock)
            {
                if (_records.TryGetValue(address, out var record) && record.Attempts >= MaxAttempts && now < record.LockoutEnd)
                {
                    remaining = record.LockoutEnd - now;
                    return true;
                }
            }

            remaining = TimeSpan.Zero;
            return false;
        }

        /// <summary>
        /// Notes down a wrong code. Returns how long they're now locked out for if this one tipped them over,
        /// otherwise null.
        /// </summary>
        public TimeSpan? RecordFailure(string address, DateTime now)
        {
            lock (_lock)
            {
                if (!_records.TryGetValue(address, out var record))
                {
                    record = new Record();
                    _records[address] = record;
                }

                // A finished lockout starts a fresh set of guesses, but we still remember it happened
                if (record.Attempts >= MaxAttempts && now >= record.LockoutEnd)
                {
                    record.Attempts = 0;
                }

                record.Attempts++;
                record.LastFailure = now;

                if (record.Attempts < MaxAttempts) return null;

                record.Lockouts++;
                long ticks = FirstLockout.Ticks << Math.Min(record.Lockouts - 1, 8);
                var lockout = TimeSpan.FromTicks(Math.Min(ticks, MaxLockout.Ticks));
                record.LockoutEnd = now + lockout;
                return lockout;
            }
        }

        /// <summary>
        /// They got the code right, so wipe the slate clean.
        /// </summary>
        public void RecordSuccess(string address)
        {
            lock (_lock)
            {
                _records.Remove(address);
            }
        }

        /// <summary>
        /// Forgets addresses that haven't guessed wrong for a while and aren't locked out.
        /// </summary>
        public void Prune(DateTime now)
        {
            lock (_lock)
            {
                var expired = new List<string>();
                foreach (var (address, record) in _records)
                {
                    if (now >= record.LockoutEnd && now - record.LastFailure > Memory)
                    {
                        expired.Add(address);
                    }
                }

                foreach (var address in expired)
                {
                    _records.Remove(address);
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _records.Clear();
            }
        }

        /// <summary>
        /// The message shown on the phone, e.g. "Try again in 5 minutes".
        /// </summary>
        public static string LockedOutMessage(TimeSpan remaining)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return $"Too many failed attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.";
        }
    }
}
