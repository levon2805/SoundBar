using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using SoundBar.Services;
using Xunit;

namespace SoundBar.Tests
{
    /// <summary>
    /// Runs the real update script on a made-up install, so we know what it does to the files, not just what it says.
    /// </summary>
    public class UpdateScriptRunTests : IDisposable
    {
        // A non-English name, like a user called Zoë or 测试 would have, to prove those folders work
        private readonly string _root = Path.Combine(Path.GetTempPath(), "SoundBarScriptRun", "Zoë 测试 " + Guid.NewGuid().ToString("N"));
        private string App => Path.Combine(_root, "Downloads");
        private string Download => Path.Combine(_root, "SoundBarUpdate");
        private string Source => Path.Combine(Download, "extracted");
        private string Started => Path.Combine(_root, "started.txt");
        private string Log => Path.Combine(_root, "settings", "update.log");
        private string Marker => Path.Combine(_root, "settings", "update-unfinished.txt");

        public UpdateScriptRunTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        /// <summary>
        /// SoundBar unpacked straight into Downloads, next to the user's own files, as "Extract here" does.
        /// </summary>
        private void MakeInstall()
        {
            Write(Path.Combine(App, "SoundBar.exe"), "old");
            Write(Path.Combine(App, "same.dll"), "old");
            Write(Path.Combine(App, "dropped.dll"), "old");
            Write(Path.Combine(App, UpdateService.FileListName), "SoundBar.exe\r\nsame.dll\r\ndropped.dll\r\n");
            Write(Path.Combine(App, "config.json"), "settings");
            Write(Path.Combine(App, "tax-return.pdf"), "mine");
            Write(Path.Combine(App, "Holiday photos", "beach.jpg"), "mine");
        }

        private void MakeUpdate()
        {
            Write(Path.Combine(Source, "SoundBar.exe"), "new");
            Write(Path.Combine(Source, "same.dll"), "new");
            Write(Path.Combine(Source, "added.dll"), "new");
            File.WriteAllLines(Path.Combine(Source, UpdateService.FileListName), UpdateService.ListFiles(Source));
        }

        /// <summary>
        /// Builds the script, swaps the steps that would touch the real PC (closing the taskbar helper, starting
        /// SoundBar) for harmless ones, runs it and waits for it to finish.
        /// </summary>
        private void RunScript()
        {
            var newFiles = Directory.Exists(Source) ? UpdateService.ListFiles(Source) : new System.Collections.Generic.List<string>();
            var stale = UpdateService.FindStaleFiles(App, newFiles);
            string script = UpdateService.BuildUpdateScript(Source, App, Download, Path.Combine(App, "SoundBar.exe"),
                restartTaskbarHelper: false, processId: 999_999_99, staleFiles: stale, logPath: Log, unfinishedMarkerPath: Marker);

            // Don't wait for, or close, the real taskbar helper
            int helperStart = script.IndexOf(":: The taskbar helper was asked", StringComparison.Ordinal);
            int helperEnd = script.IndexOf(":helperclosed", StringComparison.Ordinal);
            Assert.True(helperStart > 0 && helperEnd > helperStart);
            script = script.Remove(helperStart, helperEnd - helperStart);

            // Note that it would start SoundBar, rather than starting the made-up one
            const string start = "start \"\" \"%SB_EXE%\"";
            Assert.Contains(start, script);
            script = script.Replace(start, "echo started>\"%SB_STARTED%\"");

            var variables = UpdateService.UpdateScriptVariables(Source, App, Download, Path.Combine(App, "SoundBar.exe"), Log, Marker);
            string bat = Path.Combine(_root, "update.bat");
            File.WriteAllText(bat, script, new UTF8Encoding(false));

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{bat}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                Environment =
                {
                    ["SB_SOURCE"] = variables["SB_SOURCE"],
                    ["SB_APP"] = variables["SB_APP"],
                    ["SB_TEMP"] = variables["SB_TEMP"],
                    ["SB_EXE"] = variables["SB_EXE"],
                    ["SB_HELPER"] = variables["SB_HELPER"],
                    ["SB_LOG"] = variables["SB_LOG"],
                    ["SB_LOGDIR"] = variables["SB_LOGDIR"],
                    ["SB_MARKER"] = variables["SB_MARKER"],
                    ["SB_STARTED"] = Started,
                }
            })!;
            Assert.True(process.WaitForExit(60_000), "The update script didn't finish within a minute");
            Assert.False(File.Exists(bat), "The script should delete itself");
        }

        [Fact]
        public void AnUpdateInDownloads_KeepsTheUsersFiles_AndRemovesOnlyTheOldVersionsOwn()
        {
            MakeInstall();
            MakeUpdate();

            RunScript();

            // The user's files are all still there
            Assert.Equal("mine", File.ReadAllText(Path.Combine(App, "tax-return.pdf")));
            Assert.Equal("mine", File.ReadAllText(Path.Combine(App, "Holiday photos", "beach.jpg")));
            Assert.Equal("settings", File.ReadAllText(Path.Combine(App, "config.json")));

            // SoundBar was updated, and only its own dropped file was removed
            Assert.Equal("new", File.ReadAllText(Path.Combine(App, "SoundBar.exe")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(App, "same.dll")));
            Assert.True(File.Exists(Path.Combine(App, "added.dll")));
            Assert.False(File.Exists(Path.Combine(App, "dropped.dll")));
            Assert.Contains("added.dll", File.ReadAllText(Path.Combine(App, UpdateService.FileListName)));

            // Tidied up, started again, and nothing reported as wrong
            Assert.False(Directory.Exists(Download));
            Assert.True(File.Exists(Started));
            Assert.False(File.Exists(Marker));
        }

        [Fact]
        public void AnUpdateWhoseCopyFails_KeepsTheDownload_SaysSo_AndStillStartsSoundBar()
        {
            MakeInstall();
            Directory.CreateDirectory(Download);
            Write(Path.Combine(Download, "update.zip"), "the download");
            // No "extracted" folder, so robocopy fails every time

            var timer = Stopwatch.StartNew();
            RunScript();

            // It really waited between its 3 tries (2 seconds each time), rather than giving up at once
            Assert.True(timer.Elapsed >= TimeSpan.FromSeconds(3.5), $"Took only {timer.Elapsed.TotalSeconds:0.0}s");

            Assert.True(File.Exists(Marker));
            Assert.Contains("robocopy code", File.ReadAllText(Log));
            Assert.True(File.Exists(Path.Combine(Download, "update.zip")), "The download should be kept to try again");
            Assert.True(File.Exists(Started));

            // Nothing was deleted
            Assert.Equal("old", File.ReadAllText(Path.Combine(App, "SoundBar.exe")));
            Assert.True(File.Exists(Path.Combine(App, "dropped.dll")));
            Assert.True(File.Exists(Path.Combine(App, "tax-return.pdf")));
        }
    }
}
