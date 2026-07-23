using CSCore.CoreAudioAPI;
using SoundBar.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace SoundBar.Services
{
    /// <summary>
    /// The powerhouse that actually talks to the Windows Core Audio APIs.
    /// It hunts down active audio sessions and lets us fiddle with their volumes and mutes.
    /// </summary>
    internal class WindowsAudioMixerService : IAudioMixerService, IDisposable
    {
        private readonly ConcurrentDictionary<int, (string DisplayName, string RawProcessName, bool IsBackground, string? IconPath, DateTime LastChecked)> _processCache = new();
        private readonly MMDeviceEnumerator _enumerator;

        public WindowsAudioMixerService()
        {
            _enumerator = new MMDeviceEnumerator();
        }

        /// <summary>
        /// Rummages through Windows to find every app currently hooked into the audio system.
        /// </summary>
        public List<AudioSessionData> GetActiveAudioSessions()
        {
            var sessions = new List<AudioSessionData>();

            // We track which display names we've already seen to prevent annoying duplicates.
            var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Track what ProcessIds we see this tick to clean up stale cache entries
            var seenProcessIdsThisTick = new HashSet<int>();

            // Get the default audio device
            using (var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
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
                            bool hitCache = false;
                            (string DisplayName, string RawProcessName, bool IsBackground, string? IconPath, DateTime LastChecked) cachedApp = default;
                            
                            hitCache = _processCache.TryGetValue(processId, out cachedApp);

                            if (hitCache)
                            {
                                // If it was previously a background app, it might have spawned a window now!
                                // Re-evaluate every 5 seconds to catch games like Blue Prince that delay their window creation.
                                if (cachedApp.IsBackground && (DateTime.Now - cachedApp.LastChecked).TotalSeconds > 5)
                                {
                                    bool stillBackground = CheckIfBackgroundProcess(cachedApp.RawProcessName);
                                    _processCache[processId] = (cachedApp.DisplayName, cachedApp.RawProcessName, stillBackground, cachedApp.IconPath, DateTime.Now);
                                    cachedApp.IsBackground = stillBackground; // update local tuple
                                }

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

                            // Capitalise the first letter
                            if (displayName.Length > 0 && char.IsLower(displayName[0]))
                            {
                                displayName = char.ToUpper(displayName[0]) + displayName.Substring(1);
                            }

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

                            // Cache so we never run the slow path for this ProcessId again.
                            // We MUST do this before the addedNames check so secondary sessions get cached 
                            // and can respond to volume/mute commands!
                            _processCache[processId] = (displayName, processName, isBackground, safeIconPath, DateTime.Now);

                            // If we already have a slider for this display name, skip adding it to the UI list
                            if (addedNames.Contains(displayName)) continue;

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
            List<int> cachedIds;
            cachedIds = _processCache.Keys.ToList();

            foreach (var id in cachedIds)
            {
                if (!seenProcessIdsThisTick.Contains(id))
                {
                    _processCache.TryRemove(id, out _);
                }
            }

            return sessions;
        }

        /// <summary>
        /// Reaches into the guts of Windows to adjust the volume for a specific app.
        /// </summary>
        public void SetVolume(string processName, float level)
        {
            PerformActionOnSession(processName, (volumeControl) =>
            {
                volumeControl.MasterVolume = level;
            });
        }

        private bool CheckIfBackgroundProcess(string rawProcessName)
        {
            string safeName = rawProcessName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
            var procs = System.Diagnostics.Process.GetProcessesByName(safeName);
            bool isBackground = true;
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
                    catch { } // Ignore access denied
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
            return isBackground;
        }

        public void ClearCache()
        {
            _processCache.Clear();
        }

        /// <summary>
        /// Mutes or unmutes a specific application.
        /// </summary>
        public void SetMute(string processName, bool isMuted)
        {
            PerformActionOnSession(processName, (volumeControl) =>
            {
                volumeControl.IsMuted = isMuted;
            });
        }

        // --- Master Volume Implementation ---

        public float GetMasterVolume()
        {
            using (var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            using (var volume = AudioEndpointVolume.FromDevice(device))
            {
                return volume.MasterVolumeLevelScalar;
            }
        }

        public void SetMasterVolume(float level)
        {
            Task.Run(() =>
            {
                using (var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                using (var volume = AudioEndpointVolume.FromDevice(device))
                {
                    volume.MasterVolumeLevelScalar = level;
                }
            });
        }

        public bool GetMasterMute()
        {
            using (var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            using (var volume = AudioEndpointVolume.FromDevice(device))
            {
                return volume.IsMuted;
            }
        }

        public void SetMasterMute(bool isMuted)
        {
            Task.Run(() =>
            {
                using (var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                using (var volume = AudioEndpointVolume.FromDevice(device))
                {
                    volume.IsMuted = isMuted;
                }
            });
        }

        // System Sounds Implementation
        
        public bool GetSystemSoundsMute()
        {
            using (var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            using (var sessionManager = AudioSessionManager2.FromMMDevice(device))
            using (var sessionEnumerator = sessionManager.GetSessionEnumerator())
            {
                foreach (var session in sessionEnumerator)
                {
                    using (session)
                    using (var sessionControl = session.QueryInterface<AudioSessionControl2>())
                    {
                        if (sessionControl.IsSystemSoundSession)
                        {
                            using (var simpleVolume = session.QueryInterface<SimpleAudioVolume>())
                            {
                                return simpleVolume.IsMuted;
                            }
                        }
                    }
                }
            }
            return false;
        }

        public void SetSystemSoundsMute(bool isMuted)
        {
            Task.Run(() =>
            {
                try
                {
                    using (var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    using (var sessionManager = AudioSessionManager2.FromMMDevice(device))
                    using (var sessionEnumerator = sessionManager.GetSessionEnumerator())
                    {
                        foreach (var session in sessionEnumerator)
                        {
                            using (session)
                            using (var sessionControl = session.QueryInterface<AudioSessionControl2>())
                            {
                                if (sessionControl.IsSystemSoundSession)
                                {
                                    using (var simpleVolume = session.QueryInterface<SimpleAudioVolume>())
                                    {
                                        simpleVolume.IsMuted = isMuted;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Swallow errors on background thread
                }
            });
        }

        // Output Device Implementation

        public List<AudioDeviceModel> GetAudioDevices()
        {
            var result = new List<AudioDeviceModel>();

            string defaultDeviceId = string.Empty;
            using (var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    if (defaultDevice != null)
                    {
                        defaultDeviceId = defaultDevice.DeviceID;
                    }
                }
                
                using (var devices = _enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active))
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

            return result;
        }

        public void SetDefaultAudioDevice(string deviceId)
        {
            AudioDeviceSwitcher.SetDefaultDevice(deviceId);
        }

        public void Dispose()
        {
            _enumerator?.Dispose();
        }

        private void PerformActionOnSession(string targetProcessName, Action<SimpleAudioVolume> action)
        {
            Task.Run(() =>
            {
                try
                {
                    using (var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
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

                                bool matchFound = false;
                                if (_processCache.TryGetValue(processId, out var cached) &&
                                    string.Equals(cached.RawProcessName, targetProcessName, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchFound = true;
                                }

                                if (matchFound)
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