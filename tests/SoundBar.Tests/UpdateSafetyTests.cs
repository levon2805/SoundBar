using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SoundBar.Services;
using Xunit;

namespace SoundBar.Tests
{
    /// <summary>
    /// The update must never delete anything that isn't SoundBar's, must be able to give up or be cancelled,
    /// and mustn't restart a half-installed copy without saying so.
    /// </summary>
    [Collection("UpdateService")]
    public class UpdateSafetyTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "SoundBarUpdateSafetyTests", Guid.NewGuid().ToString("N"));

        public UpdateSafetyTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string MakeFile(string relative)
        {
            string path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
            return path;
        }

        private static string Script(string appDir = @"C:\SoundBar", params string[] stale) => UpdateService.BuildUpdateScript(
            @"C:\Temp\SoundBarUpdate\extracted", appDir, @"C:\Temp\SoundBarUpdate", Path.Combine(appDir, "SoundBar.exe"),
            restartTaskbarHelper: false, processId: 4321, staleFiles: stale,
            logPath: @"C:\Users\Test\AppData\Roaming\SoundBar\update.log",
            unfinishedMarkerPath: @"C:\Users\Test\AppData\Roaming\SoundBar\update-unfinished.txt");

        // --- Never deleting other people's files ---

        [Fact]
        public void TheScript_NeverMirrors_SoItCantDeleteOtherFilesInTheFolder()
        {
            string script = Script();
            Assert.DoesNotContain("/MIR", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/PURGE", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(" /E ", script);
        }

        [Fact]
        public void TheScript_DeletesOnlyTheOldFilesItWasGiven_AfterTheCopyWorked()
        {
            string script = Script(@"C:\SoundBar", @"old.dll", @"Assets\old.png");

            int copied = script.IndexOf(":copied", StringComparison.Ordinal);
            int deleteOld = script.IndexOf("del /f /q \"C:\\SoundBar\\old.dll\"", StringComparison.Ordinal);
            int deleteAsset = script.IndexOf("del /f /q \"C:\\SoundBar\\Assets\\old.png\"", StringComparison.Ordinal);
            Assert.True(copied > 0 && deleteOld > copied && deleteAsset > copied);

            // No other deletes of anything in the app folder
            int deletes = script.Split('\n').Count(l => l.Contains("del /f /q"));
            Assert.Equal(2, deletes);
        }

        [Fact]
        public void FindStaleFiles_WithNoListFromTheOldVersion_DeletesNothing()
        {
            MakeFile(@"app\SoundBar.exe");
            MakeFile(@"app\tax-return.pdf");
            Assert.Empty(UpdateService.FindStaleFiles(Path.Combine(_root, "app"), new[] { "SoundBar.exe" }));
        }

        [Fact]
        public void FindStaleFiles_OnlyNamesTheOldVersionsOwnFiles_ThatTheNewOneDropped()
        {
            string app = Path.Combine(_root, "Downloads");
            MakeFile(@"Downloads\SoundBar.exe");
            MakeFile(@"Downloads\old.dll");
            MakeFile(@"Downloads\Assets\old.png");
            MakeFile(@"Downloads\tax-return.pdf");
            MakeFile(@"Downloads\Holiday photos\beach.jpg");
            File.WriteAllLines(Path.Combine(app, UpdateService.FileListName), new[] { "SoundBar.exe", "old.dll", @"Assets\old.png", "gone-already.dll" });

            var stale = UpdateService.FindStaleFiles(app, new[] { "SoundBar.exe", "new.dll" });

            Assert.Equal(new[] { "old.dll", @"Assets\old.png" }, stale);
        }

        [Theory]
        [InlineData(@"..\outside.txt")]
        [InlineData(@"Assets\..\..\outside.txt")]
        [InlineData(@"C:\Windows\win.ini")]
        [InlineData(@"\outside.txt")]
        [InlineData("config.json")]
        [InlineData("crash.log")]
        [InlineData(@"Backgrounds\mine.jpg")]
        [InlineData(@"100%PATH%.txt")]
        public void FindStaleFiles_IgnoresAnythingOutsideTheFolder_OrTheUsersOwn(string listed)
        {
            string app = Path.Combine(_root, "app");
            MakeFile(@"app\SoundBar.exe");
            MakeFile(@"app\config.json");
            MakeFile(@"app\crash.log");
            MakeFile(@"app\Backgrounds\mine.jpg");
            MakeFile("outside.txt");
            MakeFile(@"app\100%PATH%.txt");
            File.WriteAllLines(Path.Combine(app, UpdateService.FileListName), new[] { listed });

            Assert.Empty(UpdateService.FindStaleFiles(app, new[] { "SoundBar.exe" }));
        }

        [Fact]
        public void ListFiles_GivesPathsInsideTheFolder()
        {
            MakeFile(@"pkg\SoundBar.exe");
            MakeFile(@"pkg\Assets\icon.png");
            Assert.Equal(new[] { @"Assets\icon.png", "SoundBar.exe" }, UpdateService.ListFiles(Path.Combine(_root, "pkg")));
        }

        // --- Waiting for the right SoundBar, and failing safely ---

        [Fact]
        public void TheScript_WaitsForThisSoundBar_NotAnyCopy()
        {
            string script = Script();
            Assert.Contains("tasklist /FI \"PID eq 4321\"", script);
            Assert.DoesNotContain("IMAGENAME eq SoundBar.exe", script);
        }

        [Fact]
        public void TheScript_RetriesAFailedCopy_ThenKeepsTheDownloadAndSaysSo()
        {
            string script = Script();

            Assert.Contains("if %copyresult% LSS 8 goto copied", script);
            Assert.Contains("if %copytries% LSS 3", script);
            Assert.Contains("update.log", script);
            Assert.Contains("update-unfinished.txt", script);

            // The download is only removed once the copy worked
            int failed = script.IndexOf("goto restart", StringComparison.Ordinal);
            int copied = script.IndexOf(":copied", StringComparison.Ordinal);
            int removeDownload = script.IndexOf("rmdir /s /q", StringComparison.Ordinal);
            Assert.True(failed > 0 && copied > failed && removeDownload > copied);

            // SoundBar starts again either way
            Assert.True(script.IndexOf("start \"\" \"C:\\SoundBar\\SoundBar.exe\"", StringComparison.Ordinal) > script.IndexOf(":restart", StringComparison.Ordinal));
        }

        [Fact]
        public void TheScript_PausesWithPing_BecauseTimeoutDoesntWaitWithoutAKeyboard()
        {
            // The script runs hidden, and timeout exits at once there, so the 15 seconds for the taskbar
            // helper to close was really no time at all
            string script = Script();
            Assert.DoesNotContain("timeout /t", script);
            Assert.Contains("ping -n 2 127.0.0.1 >nul", script);
        }

        [Fact]
        public void TheScript_WritesPercentSignsInFolderNames_SoTheyArentReadAsVariables()
        {
            string script = Script(@"C:\100% Apps\SoundBar");
            Assert.Contains("\"C:\\100%% Apps\\SoundBar\"", script);
            Assert.DoesNotContain("100% Apps", script);
        }

        [Fact]
        public void TheUnfinishedUpdateNote_IsShownOnlyOnce()
        {
            string marker = Path.Combine(_root, "update-unfinished.txt");
            Assert.False(UpdateService.TakeUnfinishedUpdateNotice(marker));

            File.WriteAllText(marker, "8");
            Assert.True(UpdateService.TakeUnfinishedUpdateNotice(marker));
            Assert.False(File.Exists(marker));
            Assert.False(UpdateService.TakeUnfinishedUpdateNotice(marker));
        }

        // --- A stalled or cancelled download ---

        /// <summary>Answers the release check, then sends the start of the download and goes quiet.</summary>
        private sealed class StallingHandler : HttpMessageHandler
        {
            public const string ReleaseJson = @"{
                ""tag_name"": ""v9.9.9"",
                ""assets"": [
                    { ""name"": ""SoundBar-v9.9.9.zip"", ""browser_download_url"": ""https://example.invalid/download.zip"" },
                    { ""name"": ""SoundBar-v9.9.9.sig"", ""browser_download_url"": ""https://example.invalid/download.sig"" }
                ]
            }";

            public TaskCompletionSource FirstBytesSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri!.Host == "api.github.com")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson) });

                var content = new StreamContent(new StallingStream(FirstBytesSent));
                content.Headers.ContentLength = 1_000_000;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
        }

        private sealed class StallingStream : Stream
        {
            private readonly TaskCompletionSource _firstBytesSent;
            private bool _sent;
            public StallingStream(TaskCompletionSource firstBytesSent) => _firstBytesSent = firstBytesSent;

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (!_sent)
                {
                    _sent = true;
                    buffer.Span[..1000].Fill(1);
                    _firstBytesSent.TrySetResult();
                    return 1000;
                }
                await Task.Delay(Timeout.Infinite, cancellationToken); // The connection has dropped
                return 0;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static string TempUpdateDir => Path.Combine(Path.GetTempPath(), "SoundBarUpdate");

        [Fact]
        public async Task AStalledDownload_GivesUpWithAFriendlyMessage_AndLeavesNothingBehind()
        {
            var original = UpdateService.StallTimeout;
            UpdateService.StallTimeout = TimeSpan.FromMilliseconds(300);
            try
            {
                var handler = new StallingHandler();
                UpdateService.SetTestMessageHandler(handler);
                var service = new UpdateService();
                Assert.True(await service.CheckForUpdatesAsync());

                double reported = -1;
                var progress = new SyncProgress(p => reported = p);

                var started = DateTime.UtcNow;
                var ex = await Assert.ThrowsAsync<UpdateFailedException>(() => service.DownloadAndApplyUpdateAsync(progress));
                Assert.Contains("stopped responding", ex.Message);
                Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));

                // The first bit arrived and was reported before it went quiet
                Assert.Equal(0.001, reported, 3);
                Assert.False(Directory.Exists(TempUpdateDir));
            }
            finally
            {
                UpdateService.StallTimeout = original;
            }
        }

        [Fact]
        public async Task CancellingTheDownload_StopsIt_AndLeavesNothingBehind()
        {
            var handler = new StallingHandler();
            UpdateService.SetTestMessageHandler(handler);
            var service = new UpdateService();
            Assert.True(await service.CheckForUpdatesAsync());

            using var cancel = new CancellationTokenSource();
            var download = service.DownloadAndApplyUpdateAsync(null, cancel.Token);
            await handler.FirstBytesSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancel.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
            Assert.False(Directory.Exists(TempUpdateDir));
        }

        /// <summary>Progress&lt;T&gt; reports later on the thread pool; this reports straight away, so the test can check it.</summary>
        private sealed class SyncProgress : IProgress<double>
        {
            private readonly Action<double> _report;
            public SyncProgress(Action<double> report) => _report = report;
            public void Report(double value) => _report(value);
        }
    }
}
