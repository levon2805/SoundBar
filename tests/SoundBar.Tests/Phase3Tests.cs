using System;
using System.Collections.Generic;
using System.IO;
using SoundBar.Helpers;
using SoundBar.Models;
using SoundBar.ViewModels;
using Xunit;

namespace SoundBar.Tests
{
    public class Phase3Tests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "SoundBarPhase3Tests", Guid.NewGuid().ToString("N"));

        public Phase3Tests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void AnAppIsInTheBackground_OnlyWhenNoneOfItsProcessesHasAWindow()
        {
            var withWindows = new HashSet<int> { 10, 20 };
            Assert.False(WindowScan.IsBackground(new[] { 5, 10 }, withWindows)); // Discord: one helper, one window
            Assert.True(WindowScan.IsBackground(new[] { 5, 6 }, withWindows));
            Assert.True(WindowScan.IsBackground(Array.Empty<int>(), withWindows));
        }

        [Fact]
        public void TheWindowScan_FindsTheTaskbarsProcess()
        {
            // Explorer always has visible windows (the taskbar) while someone's signed in
            var explorers = System.Diagnostics.Process.GetProcessesByName("explorer");
            if (explorers.Length == 0) return; // Not a normal desktop session
            var found = WindowScan.ProcessIdsWithMainWindow();
            Assert.Contains(explorers, p => found.Contains(p.Id));
            foreach (var p in explorers) p.Dispose();
        }

        [Fact]
        public void UpdatesAreCheckedAgain_OnceADay()
        {
            var start = new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc);
            Assert.True(MainViewModel.IsUpdateCheckDue(DateTime.MinValue, start)); // Never checked
            Assert.False(MainViewModel.IsUpdateCheckDue(start, start.AddHours(23)));
            Assert.True(MainViewModel.IsUpdateCheckDue(start, start.AddHours(24)));
            Assert.True(MainViewModel.IsUpdateCheckDue(start, start.AddDays(9))); // Slept for days
        }

        private string Icon(string name, DateTime lastUsed)
        {
            string path = Path.Combine(_root, name);
            File.WriteAllBytes(path, new byte[] { 1 });
            File.SetLastWriteTimeUtc(path, lastUsed);
            return path;
        }

        [Fact]
        public void TheIconTidy_RemovesOnlyLongUnusedIcons_AndLeftoverSaves()
        {
            var now = new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc);
            string recent = Icon("recent.png", now.AddDays(-10));
            string old = Icon("old.png", now.AddDays(-61));
            string freshTemp = Icon("a.png.123.tmp", now.AddHours(-1));
            string oldTemp = Icon("b.png.456.tmp", now.AddDays(-2));
            string other = Icon("notes.txt", now.AddDays(-400)); // Not ours to judge

            AudioAppModel.TidyIconFolder(_root, now);

            Assert.True(File.Exists(recent));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(freshTemp));
            Assert.False(File.Exists(oldTemp));
            Assert.True(File.Exists(other));
        }

        [Fact]
        public void UsingAnIcon_KeepsItFromBeingTidied_WithoutWritingEveryTime()
        {
            var now = new DateTime(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc);
            string old = Icon("old.png", now.AddDays(-59));
            AudioAppModel.MarkIconUsed(old, now);
            Assert.Equal(now, File.GetLastWriteTimeUtc(old));

            string today = Icon("today.png", now.AddHours(-3));
            AudioAppModel.MarkIconUsed(today, now);
            Assert.Equal(now.AddHours(-3), File.GetLastWriteTimeUtc(today)); // Left alone

            AudioAppModel.TidyIconFolder(_root, now.AddDays(30));
            Assert.True(File.Exists(old));
        }

        [Theory]
        [InlineData(96u, 300)]
        [InlineData(144u, 450)]
        [InlineData(120u, 375)]
        [InlineData(0u, 300)]
        public void TheMinimumWidth_ScalesWithTheDisplay(uint dpi, int pixels)
        {
            Assert.Equal(pixels, MinimumWindowSize.MinWidthInPixels(dpi));
        }
    }
}
