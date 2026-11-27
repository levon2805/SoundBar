using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace SoundBar.Services
{
    /// <summary>
    /// Handles everything related to keeping SoundBar up to date.
    /// It sneaks a peek at GitHub to see if there's a shiny new version, downloads it, verifies it, and applies it.
    /// </summary>
    public class UpdateService
    {
        /// <summary>
        /// The version of the app currently running. Remember to bump this before every release!
        /// </summary>
        public const string CurrentVersion = "v4.5.1";

        /// <summary>
        /// Our public key for verifying updates. This stops cheeky bad actors from hijacking the update process.
        /// </summary>
        public const string PublicKeyBase64 = "PFJTQUtleVZhbHVlPjxNb2R1bHVzPnlyK1lVWk5VVllQQ0dHb21KQ0xJaWx0M1Q0T25oUU50YlZKSHViMTR2RWNBMk5IU2JRUUdadDVaWFBCOW1LaEMxNVRRYXcyOHRDMG9PWkdva1k5ZkJoUHdMU3VxUFQ0dGRTNDBOVlRPYUlsVGNzdjhka3NNYUsyb3FMV1oraDBSaVd1bXV0VE1zUnpOVFFtK0RyNTlFeCtvSlVMSmplQ0Z2dnRndG5BdGZ6N01vUmplRDRqK3VDNXlYcGVuVUJjQWF6NFhyTGFyMUlxNWMxamZFVWgxTlVjTVU3ODZqV0U3Z0RXYlRJQ3IzY2VneFJKbi9DMnFJc0FCQjlkQWVEaHNsRENTUzJNYXJFckdodGFRNENBbC9Qdm1VT2Z6YUhxQWtkSmo5QS90Y0M5RGpBeWdBUGtiY1pMdGxFMkNxajRSWUNsK1dQZXRLQnZKM08wVFQrMXdlUT09PC9Nb2R1bHVzPjxFeHBvbmVudD5BUUFCPC9FeHBvbmVudD48L1JTQUtleVZhbHVlPg==";

        private const string RepoUrl = "https://api.github.com/repos/levon2805/SoundBar/releases/latest";
        private static HttpClient _httpClient;

        /// <summary>
        /// The list of SoundBar's own files, kept in the app folder. The next update deletes only files named
        /// in it, so it can never touch anything else that happens to share the folder.
        /// </summary>
        internal const string FileListName = "SoundBar.files";

        /// <summary>
        /// How long the download can go without receiving anything before we give up on it.
        /// </summary>
        internal static TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The version string of the newest release found on GitHub.
        /// </summary>
        public string LatestVersion { get; private set; } = string.Empty;

        /// <summary>
        /// Where we can grab the shiny new .zip file from.
        /// </summary>
        public string DownloadUrl { get; private set; } = string.Empty;

        /// <summary>
        /// Where we can grab the cryptographic signature to ensure the update is legit.
        /// </summary>
        public string SignatureUrl { get; private set; } = string.Empty;

        static UpdateService()
        {
            _httpClient = new HttpClient();
            // GitHub is a bit picky and demands a User-Agent header, so we provide one politely.
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SoundBar", "1.0"));
        }

        /// <summary>
        /// Plugs in a fake HTTP handler for our unit tests so we don't spam GitHub.
        /// </summary>
        internal static void SetTestMessageHandler(HttpMessageHandler handler)
        {
            _httpClient?.Dispose();
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SoundBar", "1.0"));
        }

        private static string SettingsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundBar");

        /// <summary>Where the update script writes what went wrong, if it couldn't install the new files.</summary>
        internal static string UpdateLogPath => Path.Combine(SettingsFolder, "update.log");

        /// <summary>Left by the update script when it couldn't install the new files, so SoundBar can say so.</summary>
        internal static string UnfinishedUpdateMarkerPath => Path.Combine(SettingsFolder, "update-unfinished.txt");

        /// <summary>
        /// True once, after an update couldn't copy its files. Clears the note so the message shows only once.
        /// </summary>
        public static bool TakeUnfinishedUpdateNotice() => TakeUnfinishedUpdateNotice(UnfinishedUpdateMarkerPath);

        internal static bool TakeUnfinishedUpdateNotice(string markerPath)
        {
            try
            {
                if (!File.Exists(markerPath)) return false;
                File.Delete(markerPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks GitHub to see if we're running an old version.
        /// </summary>
        /// <returns>True if a newer, signed version is available to download.</returns>
        public async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(RepoUrl);
                var releaseInfo = JsonSerializer.Deserialize<GithubRelease>(response);

                if (releaseInfo != null && !string.IsNullOrEmpty(releaseInfo.TagName))
                {
                    LatestVersion = releaseInfo.TagName;

                    if (Version.TryParse(LatestVersion.TrimStart('v', 'V'), out var latest) &&
                        Version.TryParse(CurrentVersion.TrimStart('v', 'V'), out var current))
                    {
                        if (latest > current)
                        {
                            var files = (releaseInfo.Assets ?? Array.Empty<GithubAsset>())
                                .Where(a => !string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(a.BrowserDownloadUrl))
                                .Select(a => (a.Name!, a.BrowserDownloadUrl!));
                            var (zipUrl, sigUrl) = PickUpdateFiles(files, RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());

                            // We strictly need both the update and its signature to proceed safely.
                            if (zipUrl != null && sigUrl != null)
                            {
                                DownloadUrl = zipUrl;
                                SignatureUrl = sigUrl;
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // The internet might be down, or GitHub is throwing a wobbly. We just quietly fail.
            }

            return false;
        }

        /// <summary>
        /// Picks this copy's download, and the signature that goes with it, from a release's files. A release can
        /// also hold builds for other systems or processors, so it doesn't just take the first .zip and .sig.
        /// </summary>
        /// <param name="files">Each file's name and download link.</param>
        /// <param name="architecture">The processor this copy is built for, like "x64" or "arm64".</param>
        internal static (string? ZipUrl, string? SigUrl) PickUpdateFiles(IEnumerable<(string Name, string Url)> files, string architecture)
        {
            var list = files.ToList();
            static bool HasSystemInName(string name) =>
                name.Contains("-windows-", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("-linux-", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("-macos-", StringComparison.OrdinalIgnoreCase);

            // Since v4.6.0 the name says which system and processor it's for: SoundBar-v4.6.0-windows-x64.zip
            var zip = list.FirstOrDefault(f => f.Name.EndsWith($"-windows-{architecture}.zip", StringComparison.OrdinalIgnoreCase));

            // Before that it was just SoundBar.zip, and only ever for Windows x64
            if (zip.Name == null && architecture == "x64")
                zip = list.FirstOrDefault(f => f.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !HasSystemInName(f.Name));

            if (zip.Name == null) return (null, null);

            // Its signature has the same name, ending .sig instead of .zip
            string sigName = zip.Name.Substring(0, zip.Name.Length - ".zip".Length) + ".sig";
            var sig = list.FirstOrDefault(f => f.Name.Equals(sigName, StringComparison.OrdinalIgnoreCase));
            return (zip.Url, sig.Name == null ? null : sig.Url);
        }

        /// <summary>
        /// Downloads the update, verifies its signature, extracts it, and triggers the clever batch script to overwrite the running app.
        /// </summary>
        /// <param name="progress">Told how much has downloaded, from 0 to 1, when the size is known.</param>
        /// <param name="cancellationToken">Cancels the download. Once the new files are ready to go in, it's too late to cancel.</param>
        /// <exception cref="UpdateFailedException">Thrown with a user-friendly message if anything goes wrong.</exception>
        /// <exception cref="OperationCanceledException">Thrown if it was cancelled. Nothing is left behind.</exception>
        public async Task DownloadAndApplyUpdateAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(DownloadUrl) || string.IsNullOrEmpty(SignatureUrl))
                throw new UpdateFailedException("There's no update ready to install. Please try again later.");

            string tempUpdateDir = Path.Combine(Path.GetTempPath(), "SoundBarUpdate");
            string zipPath = Path.Combine(tempUpdateDir, "update.zip");
            string sigPath = Path.Combine(tempUpdateDir, "update.sig");
            string extractPath = Path.Combine(tempUpdateDir, "extracted");

            // Tidy up any debris left over from previous updates
            if (Directory.Exists(tempUpdateDir))
            {
                try { Directory.Delete(tempUpdateDir, true); } catch { }
            }
            Directory.CreateDirectory(tempUpdateDir);

            string sourceDir;
            try
            {
                // Let's grab the ZIP file, then its signature
                await DownloadToFileAsync(DownloadUrl, zipPath, progress, cancellationToken);
                await DownloadToFileAsync(SignatureUrl, sigPath, null, cancellationToken);

                // Now for the security check...
                try
                {
                    string publicKeyXml = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(PublicKeyBase64));
                    using RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
                    rsa.FromXmlString(publicKeyXml);

                    byte[] sigBytes = Convert.FromBase64String(File.ReadAllText(sigPath));

                    using var sha256 = SHA256.Create();
                    using var zipFs = new FileStream(zipPath, FileMode.Open, FileAccess.Read);
                    byte[] hash = sha256.ComputeHash(zipFs);

                    RSAPKCS1SignatureDeformatter deformatter = new RSAPKCS1SignatureDeformatter(rsa);
                    deformatter.SetHashAlgorithm("SHA256");

                    if (!deformatter.VerifySignature(hash, sigBytes))
                    {
                        throw new Exception("Signature verification failed.");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Update verification failed: {ex.Message}");
                    // Something smells fishy. Let's abort.
                    throw new UpdateFailedException("The downloaded update couldn't be verified, so it wasn't installed. Your current version is untouched.", ex);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Everything is legit, so let's extract it
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                // We need to find the actual exe because sometimes zip files have root folders and sometimes they don't.
                // If there's no exe at all, the package is broken. Bail out rather than copying junk over the app.
                var exeFiles = Directory.GetFiles(extractPath, "SoundBar.exe", SearchOption.AllDirectories);
                if (!exeFiles.Any())
                {
                    throw new UpdateFailedException("The update package looks incomplete, so it wasn't installed. Your current version is untouched.");
                }
                sourceDir = Path.GetDirectoryName(exeFiles.First()) ?? extractPath;

                // The last chance to cancel: after this the taskbar helper is closed and the script takes over
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                // Cancelled, a stalled download, or a bad package: leave nothing behind
                try { Directory.Delete(tempUpdateDir, true); } catch { }
                throw;
            }

            // The real folder SoundBar's files are in. AppContext.BaseDirectory is where this app's own files
            // were loaded from, even if it was started through a shortcut or link somewhere else.
            string currentAppDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            string currentExePath = Path.Combine(currentAppDir, "SoundBar.exe");
            if (!File.Exists(currentExePath) && Environment.ProcessPath is string processPath)
            {
                currentExePath = processPath;
                currentAppDir = Path.GetDirectoryName(processPath) ?? currentAppDir;
            }

            // Record which files are SoundBar's, so the next update can tidy them up safely. It's copied in with
            // the rest. Then work out which of the old version's files the new one no longer has.
            var newFiles = ListFiles(sourceDir).Where(f => !f.Equals(FileListName, StringComparison.OrdinalIgnoreCase)).ToList();
            File.WriteAllLines(Path.Combine(sourceDir, FileListName), newFiles);
            var staleFiles = FindStaleFiles(currentAppDir, newFiles);

            string batPath = Path.Combine(Path.GetTempPath(), "SoundBar_update.bat");

            // The taskbar helper runs from this folder too, and Windows won't let files in use be replaced.
            // Ask it to close now, and the script below starts it again once the new files are in.
            bool helperWasRunning = TaskbarHelperService.FindHostWindow() != IntPtr.Zero;
            if (helperWasRunning) TaskbarHelperService.CloseRunningHelper();

            File.WriteAllText(batPath, BuildUpdateScript(sourceDir, currentAppDir, tempUpdateDir, currentExePath, helperWasRunning,
                Environment.ProcessId, staleFiles, UpdateLogPath, UnfinishedUpdateMarkerPath));

            // Fire and forget the batch script
            var processInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using (Process.Start(processInfo)) { }

            // Goodbye old version! The batch script will take it from here.
            // Use Application.Current.Exit() for a clean shutdown so Dispose chains run
            // (keyboard hooks, companion server sockets, settings flush, etc.)
            Microsoft.UI.Xaml.Application.Current.Exit();
        }

        /// <summary>
        /// Downloads one file. Gives up if nothing arrives for <see cref="StallTimeout"/>, so a dropped
        /// connection can't leave the update waiting forever.
        /// </summary>
        private static async Task DownloadToFileAsync(string url, string path, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(StallTimeout);
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0;

                await using var input = await response.Content.ReadAsStreamAsync(stall.Token);
                await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                long done = 0;
                int lastPercent = -1;
                int read;
                while ((read = await input.ReadAsync(buffer, stall.Token)) > 0)
                {
                    stall.CancelAfter(StallTimeout); // Something arrived, so start the wait again
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    done += read;

                    if (progress != null && total > 0)
                    {
                        int percent = (int)(done * 100 / total);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress.Report(Math.Min(1.0, (double)done / total));
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpdateFailedException("The download stopped responding, so the update wasn't installed. Check your internet connection and try again. Your current version is untouched.");
            }
        }

        /// <summary>
        /// Every file in a folder and its subfolders, as paths relative to it.
        /// </summary>
        internal static List<string> ListFiles(string dir) =>
            Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// The files the installed version listed as its own (in <see cref="FileListName"/>) that the new version
        /// doesn't have, and that are still there. Only these are deleted, so anything else in the folder is safe.
        /// With no list (anything installed before v4.5.2), nothing is deleted and old files are just left behind.
        /// </summary>
        internal static List<string> FindStaleFiles(string appDir, IReadOnlyCollection<string> newFiles)
        {
            var result = new List<string>();
            try
            {
                string listPath = Path.Combine(appDir, FileListName);
                if (!File.Exists(listPath)) return result;

                string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir)) + Path.DirectorySeparatorChar;
                var keep = new HashSet<string>(newFiles, StringComparer.OrdinalIgnoreCase);

                foreach (string raw in File.ReadAllLines(listPath))
                {
                    string name = raw.Trim();
                    if (name.Length == 0 || keep.Contains(name)) continue;

                    // Only plain paths inside the app folder, and never the user's own settings or pictures
                    if (Path.IsPathRooted(name) || name.Contains(':') || name.Contains('%') || name.Split('\\', '/').Contains("..")) continue;
                    if (name.Equals("config.json", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Backgrounds\\", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Backgrounds/", StringComparison.OrdinalIgnoreCase)) continue;

                    string full = Path.GetFullPath(Path.Combine(appDir, name));
                    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.Exists(full)) result.Add(name);
                }
            }
            catch (Exception ex)
            {
                // Can't read the list: leaving a few old files behind is fine
                System.Diagnostics.Debug.WriteLine($"Couldn't read the old file list: {ex.Message}");
                result.Clear();
            }
            return result;
        }

        /// <summary>
        /// The script that installs an update once SoundBar has closed. It waits for this SoundBar and the taskbar
        /// helper to close (the helper is forced closed after 15 seconds if it hasn't gone by itself), copies
        /// the new files over, deletes only the old version's own files that the new one doesn't have, then starts
        /// SoundBar again, and the helper too if it was running. If the copy keeps failing, it keeps the download,
        /// notes what happened and starts SoundBar anyway, so it can say the update didn't finish.
        /// </summary>
        /// <param name="processId">This SoundBar's process, so another copy running elsewhere can't keep it waiting. 0 waits for any SoundBar.</param>
        /// <param name="staleFiles">Files in the app folder to delete once the copy has worked, relative to it.</param>
        internal static string BuildUpdateScript(string sourceDir, string appDir, string tempUpdateDir, string exePath, bool restartTaskbarHelper,
            int processId = 0, IReadOnlyList<string>? staleFiles = null, string? logPath = null, string? unfinishedMarkerPath = null)
        {
            static string EscapeForBatch(string path)
            {
                string p = path.EndsWith("\\") ? path + "\\" : path;
                // %% is how a batch file writes a plain %, so a folder like "100% Apps" isn't read as a variable
                return "\"" + p.Replace("\"", "\"\"").Replace("%", "%%") + "\"";
            }

            logPath ??= UpdateLogPath;
            unfinishedMarkerPath ??= UnfinishedUpdateMarkerPath;
            string logDir = Path.GetDirectoryName(logPath) ?? Path.GetTempPath();

            string helperImage = Shared.TaskbarHelperInfo.ImageName;
            string helperPath = Path.Combine(appDir, Shared.TaskbarHelperInfo.ExeName);
            string restartHelper = restartTaskbarHelper
                ? $"if exist {EscapeForBatch(helperPath)} start \"\" {EscapeForBatch(helperPath)}"
                : ":: (The taskbar helper wasn't running, so it isn't started)";

            string waitFilter = processId > 0 ? $"PID eq {processId}" : "IMAGENAME eq SoundBar.exe";

            var deletes = new System.Text.StringBuilder();
            foreach (string name in staleFiles ?? Array.Empty<string>())
            {
                string full = EscapeForBatch(Path.Combine(appDir, name));
                deletes.AppendLine($"if exist {full} del /f /q {full}");
            }
            if (deletes.Length == 0) deletes.AppendLine(":: (No old files to remove)");

            return $$"""
@echo off
:: The script is saved as UTF-8. Without this, Command Prompt reads it in the old code page and
:: can't find folders with accented or non-English letters (the update unpacks under the user's name).
chcp 65001 >nul
echo Updating SoundBar... Please wait.

:: Pauses use ping rather than timeout: this script runs hidden with no keyboard, and timeout gives up at
:: once without one, which turned every wait below into a busy loop.

:: Wait for this copy of SoundBar to close. Only this one: another copy running from a different
:: folder mustn't keep the update waiting forever.
:waitloop
tasklist /FI "{{waitFilter}}" 2>NUL | find /I "SoundBar.exe">NUL
if "%ERRORLEVEL%"=="0" (
    ping -n 2 127.0.0.1 >nul
    goto waitloop
)

:: The taskbar helper was asked to close. Give it up to 15 seconds, then make sure.
set /a helperwait=0
:waithelper
tasklist /FI "IMAGENAME eq {{helperImage}}" 2>NUL | find /I /N "{{helperImage}}">NUL
if "%ERRORLEVEL%"=="0" (
    if %helperwait% GEQ 15 (
        taskkill /F /IM {{helperImage}} >NUL 2>&1
        ping -n 2 127.0.0.1 >nul
        goto helperclosed
    )
    set /a helperwait+=1
    ping -n 2 127.0.0.1 >nul
    goto waithelper
)
:helperclosed

:: Copy the new files over the old ones. /E copies and overwrites but never deletes, so nothing else in the
:: folder can be lost (it may not be a folder of its own, like Downloads). Old files are removed below, by name.
:: We exclude config/user data that lives alongside the exe.
:: A locked file (antivirus scanning it, say) makes robocopy fail, so try up to 3 times.
set /a copytries=0
:copyfiles
robocopy {{EscapeForBatch(sourceDir)}} {{EscapeForBatch(appDir)}} /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XF "config.json" "*.log" /XD "Backgrounds"
set copyresult=%ERRORLEVEL%
:: Robocopy exit codes 0-7 are success; 8+ are errors.
if %copyresult% LSS 8 goto copied
set /a copytries+=1
if %copytries% LSS 3 (
    ping -n 3 127.0.0.1 >nul
    goto copyfiles
)

:: It still didn't work. Keep the download, so nothing is lost and it can be tried again, note what
:: happened, and start SoundBar anyway so it can tell the user.
if not exist {{EscapeForBatch(logDir)}} mkdir {{EscapeForBatch(logDir)}}
>>{{EscapeForBatch(logPath)}} echo %date% %time% The update couldn't copy its files (robocopy code %copyresult%). The download was kept in {{EscapeForBatch(tempUpdateDir)}}.
>{{EscapeForBatch(unfinishedMarkerPath)}} echo %copyresult%
goto restart

:copied
:: Remove the files the old version listed as its own that the new version doesn't have
{{deletes.ToString().TrimEnd()}}

:: Clean up the temp directory
rmdir /s /q {{EscapeForBatch(tempUpdateDir)}}

:restart
:: Restart the application, and the taskbar helper if it was running
start "" {{EscapeForBatch(exePath)}}
{{restartHelper}}

:: Delete this batch file
del "%~f0"
""";
        }

        // --- Helper classes just to read the GitHub API JSON ---

        private class GithubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("assets")]
            public GithubAsset[]? Assets { get; set; }
        }

        private class GithubAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }

    /// <summary>
    /// Thrown when an update can't be downloaded, checked or installed.
    /// The message is friendly enough to show straight to the user.
    /// </summary>
    public class UpdateFailedException : Exception
    {
        public UpdateFailedException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
