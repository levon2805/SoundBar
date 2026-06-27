using CSCore.CoreAudioAPI;
using SoundBar.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SoundBar.Services
{
    internal class WindowsAudioMixerService : IAudioMixerService
    {
        public List<AudioAppModel> GetActiveAudioSessions()
        {
            var apps = new List<AudioAppModel>();

            // Track which NAMES we have processed to prevent duplicates
            var addedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Get the default audio device
            using (var enumerator = new MMDeviceEnumerator())
            using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                // Get the session manager for that device
                using (var sessionManager = AudioSessionManager2.FromMMDevice(device))
                using (var sessionEnumerator = sessionManager.GetSessionEnumerator())
                {
                    // Loop through the audio sessions found
                    foreach (var session in sessionEnumerator)
                    {
                        using (var sessionControl = session.QueryInterface<AudioSessionControl2>())
                        using (var simpleVolume = session.QueryInterface<SimpleAudioVolume>())
                        {
                            var process = sessionControl.Process;

                            // Ignore Idle (ID 0) and dead processes
                            if (process == null || process.Id == 0) continue;

                            // Ignore expired sessions, but allow Inactive sessions so users can adjust volume of paused/silent apps (matches Windows Volume Mixer)
                            if (sessionControl.SessionState == AudioSessionState.AudioSessionStateExpired) continue;

                            string processName = process.ProcessName;
                            if (string.IsNullOrEmpty(processName)) continue;

                            // Identify if this is a background process
                            // Check ALL processes with this name. If ANY have a main window, it's not a background app.
                            // (E.g. Discord's audio engine is headless, but the main Discord.exe UI has a window)
                            bool isBackground = true;
                            try
                            {
                                string safeName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                                var procs = System.Diagnostics.Process.GetProcessesByName(safeName);
                                foreach (var p in procs)
                                {
                                    if (p.MainWindowHandle != IntPtr.Zero)
                                    {
                                        isBackground = false;
                                        break;
                                    }
                                }
                            }
                            catch
                            {
                                // Fallback if access is denied
                                isBackground = process.MainWindowHandle == IntPtr.Zero;
                            }

                            // If we already have a slider for this App Name, skip it to prevent duplicates
                            if (addedProcessNames.Contains(processName)) continue;

                            string? safeIconPath = GetExecutablePathSafely(process);

                            // Clean up display names for Unreal/Unity engine games or common wrappers
                            string displayName = processName;
                            string[] badSuffixes = new[] { "-Win64-Shipping", "-Win64", "-shell-ng" };
                            foreach (string suffix in badSuffixes)
                            {
                                if (displayName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                                {
                                    displayName = displayName.Substring(0, displayName.Length - suffix.Length);
                                    break;
                                }
                            }

                            // Also capitalize the first letter to make it look nicer if it's all lowercase
                            if (displayName.Length > 0 && char.IsLower(displayName[0]))
                            {
                                displayName = char.ToUpper(displayName[0]) + displayName.Substring(1);
                            }

                            var newApp = new AudioAppModel(this)
                            {
                                ProcessId = process.Id,
                                IsBackgroundApp = isBackground,
                                Name = displayName,
                                Volume = simpleVolume.MasterVolume,
                                IsMuted = simpleVolume.IsMuted,
                                IconPath = safeIconPath
                            };

                            apps.Add(newApp);
                            addedProcessNames.Add(processName);
                        }
                    }
                }
            }
            return apps;
        }

        public void SetVolume(string processName, float level)
        {
            PerformActionOnSession(processName, (volumeControl) =>
            {
                volumeControl.MasterVolume = level;
            });
        }

        public void SetMute(string processName, bool isMuted)
        {
            PerformActionOnSession(processName, (volumeControl) =>
            {
                volumeControl.IsMuted = isMuted;
            });
        }

        // Master Volume Implementation

        public float GetMasterVolume()
        {
            using (var enumerator = new MMDeviceEnumerator())
            using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            using (var volume = AudioEndpointVolume.FromDevice(device))
            {
                return volume.MasterVolumeLevelScalar;
            }
        }

        public void SetMasterVolume(float level)
        {
            Task.Run(() =>
            {
                using (var enumerator = new MMDeviceEnumerator())
                using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                using (var volume = AudioEndpointVolume.FromDevice(device))
                {
                    volume.MasterVolumeLevelScalar = level;
                }
            });
        }

        public bool GetMasterMute()
        {
            using (var enumerator = new MMDeviceEnumerator())
            using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            using (var volume = AudioEndpointVolume.FromDevice(device))
            {
                return volume.IsMuted;
            }
        }

        public void SetMasterMute(bool isMuted)
        {
            Task.Run(() =>
            {
                using (var enumerator = new MMDeviceEnumerator())
                using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                using (var volume = AudioEndpointVolume.FromDevice(device))
                {
                    volume.IsMuted = isMuted;
                }
            });
        }

        private void PerformActionOnSession(string targetProcessName, Action<SimpleAudioVolume> action)
        {
            Task.Run(() =>
            {
                try
                {
                    using (var enumerator = new MMDeviceEnumerator())
                    using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    using (var sessionManager = AudioSessionManager2.FromMMDevice(device))
                    using (var sessionEnumerator = sessionManager.GetSessionEnumerator())
                    {
                        foreach (var session in sessionEnumerator)
                        {
                            using (var sessionControl = session.QueryInterface<AudioSessionControl2>())
                            using (var simpleVolume = session.QueryInterface<SimpleAudioVolume>())
                            {
                                if (sessionControl.Process != null && string.Equals(sessionControl.Process.ProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Apply action to all matching sessions
                                    action(simpleVolume);
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Take errors on background thread to prevent crashing the app
                }
            });
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hHandle);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private static string? GetExecutablePathSafely(System.Diagnostics.Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Fallback for Anti-Cheat protected processes (like Valorant / Vanguard)
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        var buffer = new System.Text.StringBuilder(1024);
                        uint size = (uint)buffer.Capacity;
                        if (QueryFullProcessImageName(hProcess, 0, buffer, ref size))
                        {
                            return buffer.ToString();
                        }
                    }
                    finally
                    {
                        CloseHandle(hProcess);
                    }
                }
                return null;
            }
        }
    }
}