using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        public const string CurrentVersion = "v3.0.0";
        
        /// <summary>
        /// Our public key for verifying updates. This stops cheeky bad actors from hijacking the update process.
        /// </summary>
        public const string PublicKeyBase64 = "PFJTQUtleVZhbHVlPjxNb2R1bHVzPnh6bTBBMENEb0xMdC96aDVSazhhb0R0Yk5zdUs0QWNQOEdaTUpSMHRadUhrKzN2M2pUZUhQYVRlbTQ3OFlrZk5nVzRBeTVDa05PNHhjSGVMM0tCSGk5dDlKbjIrdXEza2VaV2NsZFdPWCtESXJqbWUrbk1YSDZURDZzMDZ3VGpBM0RWWTYxOHRXNmQvdnNJTWc5emlEUUxKSFl5RGNPbXhkODVwRkJveTkyNE1YRFdvbFhpZUx6YmN6M2p0K1IweDcySzdmOGsrVHRoNzlpRzJOeVVHZGQ2Rng1Y2lzRzlUaGd3emhIczc2eVh2VGV5UHhEaElWVCt0eXZGSlBUaTEzaDhBRXhWdkhaVVJnVVU4RWxsZ2ZTWTRNNCs0NUNDYkRxc2dPVmR4RGNvTkNOa1YrOU80d2d0WnZJNkI3bzFnUzdtekdJN1JpWklvWkxIbnVtYnBlUT09PC9Nb2R1bHVzPjxFeHBvbmVudD5BUUFCPC9FeHBvbmVudD48L1JTQUtleVZhbHVlPg==";

        private const string RepoUrl = "https://api.github.com/repos/levon2805/SoundBar/releases/latest";
        private static HttpClient _httpClient;

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
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SoundBar", "1.0"));
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
                            var zipAsset = releaseInfo.Assets?.FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                            var sigAsset = releaseInfo.Assets?.FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase));
                            
                            // We strictly need both the update and its signature to proceed safely.
                            if (zipAsset != null && zipAsset.BrowserDownloadUrl != null && sigAsset != null && sigAsset.BrowserDownloadUrl != null)
                            {
                                DownloadUrl = zipAsset.BrowserDownloadUrl;
                                SignatureUrl = sigAsset.BrowserDownloadUrl;
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
        /// Downloads the update, verifies its signature, extracts it, and triggers the clever batch script to overwrite the running app.
        /// </summary>
        public async Task DownloadAndApplyUpdateAsync()
        {
            if (string.IsNullOrEmpty(DownloadUrl) || string.IsNullOrEmpty(SignatureUrl)) return;

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

            // Let's grab the ZIP file
            using var response = await _httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using (var fs = new FileStream(zipPath, FileMode.Create))
            {
                await response.Content.CopyToAsync(fs);
            }

            // Let's grab the signature file
            using var sigResponse = await _httpClient.GetAsync(SignatureUrl, HttpCompletionOption.ResponseHeadersRead);
            sigResponse.EnsureSuccessStatusCode();

            using (var sigFs = new FileStream(sigPath, FileMode.Create))
            {
                await sigResponse.Content.CopyToAsync(sigFs);
            }

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
                // Something smells fishy. Let's abort and clean up.
                try { Directory.Delete(tempUpdateDir, true); } catch { }
                return;
            }

            // Everything is legit, so let's extract it
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            // We need to find the actual exe because sometimes zip files have root folders and sometimes they don't.
            string sourceDir = extractPath;
            var exeFiles = Directory.GetFiles(extractPath, "SoundBar.exe", SearchOption.AllDirectories);
            if (exeFiles.Any())
            {
                sourceDir = Path.GetDirectoryName(exeFiles.First()) ?? extractPath;
            }

            // We write a sneaky batch script to the temp folder. This will run independently, wait for us to close, and copy the files over.
            string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? AppDomain.CurrentDomain.BaseDirectory;
            string currentAppDir = Path.GetDirectoryName(currentExePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string batPath = Path.Combine(Path.GetTempPath(), "SoundBar_update.bat");

            string batContent = $$"""
@echo off
echo Updating SoundBar... Please wait.

:waitloop
tasklist /FI "IMAGENAME eq SoundBar.exe" 2>NUL | find /I /N "SoundBar.exe">NUL
if "%ERRORLEVEL%"=="0" (
    timeout /t 1 /nobreak >nul
    goto waitloop
)

:: Copy all files from the extracted update to the current app directory, overwriting old ones
xcopy "{{sourceDir}}\*" "{{currentAppDir}}\" /s /y /q

:: Clean up the temp directory
rmdir /s /q "{{tempUpdateDir}}"

:: Create a desktop shortcut if it doesn't exist
set "LNK_PATH=%USERPROFILE%\Desktop\SoundBar.lnk"
if not exist "%LNK_PATH%" (
    powershell -Command "$wshell = New-Object -ComObject WScript.Shell; $s = $wshell.CreateShortcut('%LNK_PATH%'); $s.TargetPath = '{{currentExePath}}'; $s.WorkingDirectory = '{{currentAppDir}}'; $s.Save()"
)

:: Restart the application
start "" "{{currentExePath}}"

:: Delete this batch file
del "%~f0"
""";
            File.WriteAllText(batPath, batContent);

            // Fire and forget the batch script
            var processInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(processInfo);

            // Goodbye old version! The batch script will take it from here.
            Environment.Exit(0);
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
}
