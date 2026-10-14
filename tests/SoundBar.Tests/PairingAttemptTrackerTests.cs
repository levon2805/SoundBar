using System;
using SoundBar.Services;
using Xunit;

namespace SoundBar.Tests
{
    public class PairingAttemptTrackerTests
    {
        private const string Phone = "192.168.1.50";
        private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static TimeSpan? FailTimes(PairingAttemptTracker tracker, int count, DateTime at)
        {
            TimeSpan? lockout = null;
            for (int i = 0; i < count; i++) lockout = tracker.RecordFailure(Phone, at);
            return lockout;
        }

        [Fact]
        public void FourWrongCodes_DoNotLockOut()
        {
            var tracker = new PairingAttemptTracker();

            Assert.Null(FailTimes(tracker, 4, Start));
            Assert.False(tracker.IsLockedOut(Phone, Start, out _));
        }

        [Fact]
        public void FifthWrongCode_LocksOutForFiveMinutes()
        {
            var tracker = new PairingAttemptTracker();

            Assert.Equal(TimeSpan.FromMinutes(5), FailTimes(tracker, 5, Start));
            Assert.True(tracker.IsLockedOut(Phone, Start.AddMinutes(4), out var remaining));
            Assert.Equal(TimeSpan.FromMinutes(1), remaining);
            Assert.False(tracker.IsLockedOut(Phone, Start.AddMinutes(5), out _));
        }

        [Fact]
        public void WrongCodes_AreNotForgottenAfterThirtySeconds()
        {
            // The old cleanup wiped the count every ~30 seconds, so slow guessing never got locked out
            var tracker = new PairingAttemptTracker();

            FailTimes(tracker, 4, Start);
            tracker.Prune(Start.AddSeconds(45));

            Assert.NotNull(tracker.RecordFailure(Phone, Start.AddSeconds(50)));
        }

        [Fact]
        public void RepeatLockouts_DoubleEachTime_UpToAnHour()
        {
            var tracker = new PairingAttemptTracker();
            var now = Start;
            var expected = new[] { 5, 10, 20, 40, 60, 60 };

            foreach (int minutes in expected)
            {
                Assert.Equal(TimeSpan.FromMinutes(minutes), FailTimes(tracker, 5, now));
                now += TimeSpan.FromMinutes(minutes); // Wait out the lockout before guessing again
            }
        }

        [Fact]
        public void CorrectCode_WipesTheSlateClean()
        {
            var tracker = new PairingAttemptTracker();

            FailTimes(tracker, 5, Start);
            tracker.RecordSuccess(Phone);

            Assert.False(tracker.IsLockedOut(Phone, Start, out _));
            Assert.Null(FailTimes(tracker, 4, Start));
        }

        [Fact]
        public void Prune_ForgetsAddressesThatStoppedGuessingAnHourAgo()
        {
            var tracker = new PairingAttemptTracker();

            FailTimes(tracker, 5, Start);
            tracker.Prune(Start.AddMinutes(61));

            // Treated as a fresh start: back to a 5 minute lockout rather than 10
            Assert.Equal(TimeSpan.FromMinutes(5), FailTimes(tracker, 5, Start.AddMinutes(62)));
        }

        [Fact]
        public void OtherAddresses_AreNotAffected()
        {
            var tracker = new PairingAttemptTracker();

            FailTimes(tracker, 5, Start);

            Assert.False(tracker.IsLockedOut("192.168.1.51", Start, out _));
        }

        [Theory]
        [InlineData(300, "Too many failed attempts. Try again in 5 minutes.")]
        [InlineData(61, "Too many failed attempts. Try again in 2 minutes.")]
        [InlineData(20, "Too many failed attempts. Try again in 1 minute.")]
        public void LockedOutMessage_RoundsUpToWholeMinutes(int seconds, string expected)
        {
            Assert.Equal(expected, PairingAttemptTracker.LockedOutMessage(TimeSpan.FromSeconds(seconds)));
        }
    }
}
