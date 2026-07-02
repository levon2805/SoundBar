using CSCore.CoreAudioAPI;
using SoundBar.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace SoundBar.Services
{
    internal class WindowsAudioMixerService : IAudioMixerService, IDisposable
    {
        // Cache of ProcessId -> App Info to prevent allocating heavy Process objects every tick
        private readonly Dictionary<int, (string DisplayName, string RawProcessName, bool IsBackground, string? IconPath)> _processCache = new();

        public List<AudioSessionData> GetActiveAudioSessions()
        {
            var sessions = new List<AudioSessionData>();

            // Track which display names we have processed to prevent duplicates
            var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Track what ProcessIds we see this tick to clean up stale cache entries
            var seenProcessIdsThisTick = new HashSet<int>();

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
                        // Dispose the raw session COM object to prevent COM handle leaks
                        using (session)
                        using (var sessionControl = session.QueryInterface<AudioSessionControl2>())
                        using (var simpleVolume = session.QueryInterface<SimpleAudioVolume>())
                        {
                            // Ignore expired sessions
                            if (sessionControl.SessionState == AudioSessionState.AudioSessionStateExpired) continue;

                            // Use ProcessID directly — no System.Diagnostics.Process allocation
                            int processId = sessionControl.ProcessID;
                            if (processId == 0) continue;
                            
                            seenProcessIdsThisTick.Add(processId);

                            // FAST PATH: Use cached process info (most ticks hit this)
                            if (_processCache.TryGetValue(processId, out var cachedApp))
                            {
                                if (addedNames.Contains(cachedApp.DisplayName)) continue;

                                sessions.Add(new AudioSessionData
                                {
                                    ProcessId = processId,
                                    DisplayName = cachedApp.DisplayName,
                                    RawProcessName = cachedApp.RawProcessName,
                                    IsBackgroundApp = cachedApp.IsBackground,
                                    Volume = simpleVolume.MasterVolume,
                                    IsMuted = simpleVolume.IsMuted,
                                    IconPath = cachedApp.IconPath
                                });

                                addedNames.Add(cachedApp.DisplayName);
                                continue;
                            }

                            // SLOW PATH: First time seeing this ProcessId, resolve its name
                            using var process = sessionControl.Process;
                            if (process == null) continue;

                            string processName = process.ProcessName;
                            if (string.IsNullOrEmpty(processName)) continue;

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

                            // Capitalize the first letter
                            if (displayName.Length > 0 && char.IsLower(displayName[0]))
                            {
                                displayName = char.ToUpper(displayName[0]) + displayName.Substring(1);
                            }

                            // If we already have a slider for this display name, skip it
                            if (addedNames.Contains(displayName)) continue;

                            bool isBackground = true;
                            string? safeIconPath = null;

                            // Identify if this is a background process
                            try
                            {
                                string safeName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                                var procs = System.Diagnostics.Process.GetProcessesByName(safeName);
                                try
                                {
                                    foreach (var p in procs)
                                    {
                                        try
                                        {
                                            if (p.MainWindowHandle != IntPtr.Zero)
                                            {
                                                isBackground = false;
                                            }
                                        }
                                        catch
                                        {
                                            // Ignore access denied for this specific process instance
                                        }
                                    }
                                }
                                finally
                                {
                                    foreach (var p in procs) p.Dispose();
                                }
                            }
                            catch
                            {
                                try
                                {
                                    isBackground = process.MainWindowHandle == IntPtr.Zero;
                                }
                                catch
                                {
                                    isBackground = true;
                                }
                            }

                            safeIconPath = GetExecutablePathSafely(process);

                            // Cache so we never run the slow path for this ProcessId again
                            _processCache[processId] = (displayName, processName, isBackground, safeIconPath);

                            sessions.Add(new AudioSessionData
                            {
                                ProcessId = processId,
                                DisplayName = displayName,
                                RawProcessName = processName,
                                IsBackgroundApp = isBackground,
                                Volume = simpleVolume.MasterVolume,
                                IsMuted = simpleVolume.IsMuted,
                                IconPath = safeIconPath
                            });

                            addedNames.Add(displayName);
                        }
                    }
                }
            }

            // Cleanup dead processes from cache
            var cachedIds = _processCache.Keys.ToList();
            foreach (var id in cachedIds)
            {
                if (!seenProcessIdsThisTick.Contains(id))
                {
                    _processCache.Remove(id);
                }
            }

            return sessions;
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

        // Output Device Implementation

        public List<AudioDeviceModel> GetAudioDevices()
        {
            var result = new List<AudioDeviceModel>();

            using (var enumerator = new MMDeviceEnumerator())
            {
                string defaultDeviceId = string.Empty;
                using (var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    if (defaultDevice != null)
                    {
                        defaultDeviceId = defaultDevice.DeviceID;
                    }
                }
                
                using (var devices = enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active))
                {
                    foreach (var device in devices)
                    {
                        result.Add(new AudioDeviceModel
                        {
                            Id = device.DeviceID,
                            Name = device.FriendlyName,
                            IsDefault = device.DeviceID == defaultDeviceId
                        });
                        device.Dispose(); // Dispose each individual device after processing
                    }
                }
            }

            return result;
        }

        public void SetDefaultAudioDevice(string deviceId)
        {
            AudioDeviceSwitcher.SetDefaultDevice(deviceId);
        }

        public void Dispose()
        {
            // No longer need to dispose CoreAudioController
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
                            // Dispose the raw session COM object to prevent COM handle leaks
                            using (session)
                            using (var sessionControl = session.QueryInterface<AudioSessionControl2>())
                            using (var simpleVolume = session.QueryInterface<SimpleAudioVolume>())
                            {
                                // Match by ProcessID using the cache — zero Process object allocations
                                int processId = sessionControl.ProcessID;
                                if (processId == 0) continue;

                                if (_processCache.TryGetValue(processId, out var cached) &&
                                    string.Equals(cached.RawProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase))
                                {
                                    action(simpleVolume);
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Swallow errors on background thread to prevent crashing the app
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