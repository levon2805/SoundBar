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

namespace SoundBar.Services
{
    public class UpdateService
    {
        // Change this every time releasing a new version
        public const string CurrentVersion = "v1.5.1";
        
        private const string RepoUrl = "https://api.github.com/repos/levon2805/SoundBar/releases/latest";
        private static readonly HttpClient _httpClient = new HttpClient();

        public string LatestVersion { get; private set; } = string.Empty;
        public string DownloadUrl { get; private set; } = string.Empty;

        public UpdateService()
        {
            // GitHub API requires a User-Agent header
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
                        var asset = releaseInfo.Assets?.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                        if (asset != null)
                        {
                            DownloadUrl = asset.BrowserDownloadUrl;
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
            if (string.IsNullOrEmpty(DownloadUrl)) return;

            string tempUpdateDir = Path.Combine(Path.GetTempPath(), "SoundBarUpdate");
            string zipPath = Path.Combine(tempUpdateDir, "update.zip");
            string extractPath = Path.Combine(tempUpdateDir, "extracted");

            // Clean up any old update folders
            if (Directory.Exists(tempUpdateDir))
            {
                try { Directory.Delete(tempUpdateDir, true); } catch { }
            }
            Directory.CreateDirectory(tempUpdateDir);

            // Download the ZIP
            using var response = await _httpClient.GetAsync(DownloadUrl);
            response.EnsureSuccessStatusCode();

            using (var fs = new FileStream(zipPath, FileMode.Create))
            {
                await response.Content.CopyToAsync(fs);
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
