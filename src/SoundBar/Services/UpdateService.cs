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
    public class UpdateService
    {
        // Change this every time releasing a new version
        public const string CurrentVersion = "v2.3.2";
        
        public const string PublicKeyBase64 = "PFJTQUtleVZhbHVlPjxNb2R1bHVzPnh6bTBBMENEb0xMdC96aDVSazhhb0R0Yk5zdUs0QWNQOEdaTUpSMHRadUhrKzN2M2pUZUhQYVRlbTQ3OFlrZk5nVzRBeTVDa05PNHhjSGVMM0tCSGk5dDlKbjIrdXEza2VaV2NsZFdPWCtESXJqbWUrbk1YSDZURDZzMDZ3VGpBM0RWWTYxOHRXNmQvdnNJTWc5emlEUUxKSFl5RGNPbXhkODVwRkJveTkyNE1YRFdvbFhpZUx6YmN6M2p0K1IweDcySzdmOGsrVHRoNzlpRzJOeVVHZGQ2Rng1Y2lzRzlUaGd3emhIczc2eVh2VGV5UHhEaElWVCt0eXZGSlBUaTEzaDhBRXhWdkhaVVJnVVU4RWxsZ2ZTWTRNNCs0NUNDYkRxc2dPVmR4RGNvTkNOa1YrOU80d2d0WnZJNkI3bzFnUzdtekdJN1JpWklvWkxIbnVtYnBlUT09PC9Nb2R1bHVzPjxFeHBvbmVudD5BUUFCPC9FeHBvbmVudD48L1JTQUtleVZhbHVlPg==";

        private const string RepoUrl = "https://api.github.com/repos/levon2805/SoundBar/releases/latest";
        private static HttpClient _httpClient;

        public string LatestVersion { get; private set; } = string.Empty;
        public string DownloadUrl { get; private set; } = string.Empty;
        public string SignatureUrl { get; private set; } = string.Empty;

        static UpdateService()
        {
            _httpClient = new HttpClient();
            // GitHub API requires a User-Agent header
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SoundBar", "1.0"));
        }

        internal static void SetTestMessageHandler(HttpMessageHandler handler)
        {
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SoundBar", "1.0"));
        }

        public async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(RepoUrl);
                var releaseInfo = JsonSerializer.Deserialize<GithubRelease>(response);

                if (releaseInfo != null && !string.IsNullOrEmpty(releaseInfo.TagName))
                {
                    LatestVersion = releaseInfo.TagName;

                    if (LatestVersion != CurrentVersion)
                    {
                        var zipAsset = releaseInfo.Assets?.FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                        var sigAsset = releaseInfo.Assets?.FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase));
                        
                        if (zipAsset != null && zipAsset.BrowserDownloadUrl != null && sigAsset != null && sigAsset.BrowserDownloadUrl != null)
                        {
                            DownloadUrl = zipAsset.BrowserDownloadUrl;
                            SignatureUrl = sigAsset.BrowserDownloadUrl;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore network errors or parsing errors
            }

            return false;
        }

        public async Task DownloadAndApplyUpdateAsync()
        {
            if (string.IsNullOrEmpty(DownloadUrl) || string.IsNullOrEmpty(SignatureUrl)) return;

            string tempUpdateDir = Path.Combine(Path.GetTempPath(), "SoundBarUpdate");
            string zipPath = Path.Combine(tempUpdateDir, "update.zip");
            string sigPath = Path.Combine(tempUpdateDir, "update.sig");
            string extractPath = Path.Combine(tempUpdateDir, "extracted");

            // Clean up any old update folders
            if (Directory.Exists(tempUpdateDir))
            {
                try { Directory.Delete(tempUpdateDir, true); } catch { }
            }
            Directory.CreateDirectory(tempUpdateDir);

            // Download the ZIP
            using var response = await _httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using (var fs = new FileStream(zipPath, FileMode.Create))
            {
                await response.Content.CopyToAsync(fs);
            }

            // Download the SIG
            using var sigResponse = await _httpClient.GetAsync(SignatureUrl, HttpCompletionOption.ResponseHeadersRead);
            sigResponse.EnsureSuccessStatusCode();

            using (var sigFs = new FileStream(sigPath, FileMode.Create))
            {
                await sigResponse.Content.CopyToAsync(sigFs);
            }

            // Verify signature
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
                // Cleanup and abort update process
                try { Directory.Delete(tempUpdateDir, true); } catch { }
                return;
            }

            // Extract the ZIP
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            // Find the actual folder containing SoundBar.exe inside the extracted zip
            // Sometimes zips contain a root folder, sometimes just the files. We need the directory containing SoundBar.exe.
            string sourceDir = extractPath;
            var exeFiles = Directory.GetFiles(extractPath, "SoundBar.exe", SearchOption.AllDirectories);
            if (exeFiles.Any())
            {
                sourceDir = Path.GetDirectoryName(exeFiles.First()) ?? extractPath;
            }

            // Create the updater batch script outside the temp folder so it doesn't delete itself
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

            // Execute the batch script invisibly
            var processInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(processInfo);

            // Terminate this application so the batch script can overwrite the files
            Environment.Exit(0);
        }

        // Helper classes for JSON deserialization
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
