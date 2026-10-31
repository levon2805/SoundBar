using CSCore.CoreAudioAPI;
using SoundBar.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        // Device names barely ever change, and reading them means opening each device's property store,
        // so we remember them instead of asking every second. The refresh button clears this.
        private readonly ConcurrentDictionary<string, string> _deviceNameCache = new();

        // The system sounds mute state, noted down while walking the sessions so GetSystemSoundsMute
        // doesn't have to walk them all over again a moment later. Guarded by _pollLock.
        private bool _systemSoundsMutedSnapshot;
        private bool _hasSystemSoundsSnapshot;
        private long _systemSoundsSnapshotTicks;
        private const long SystemSoundsSnapshotMaxAgeMs = 1500;

        /// <summary>
        /// Rummages through Windows to find every app currently hooked into the audio system.
        /// </summary>
        private readonly object _pollLock = new object();
        public List<AudioSessionData> GetActiveAudioSessions()
        {
            lock (_pollLock)
            {
            var sessions = new List<AudioSessionData>();

            var _addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var _seenProcessIdsThisTick = new HashSet<int>();
            bool? systemSoundsMuted = null;

            // Listing processes means snapshotting every process on the PC, so take at most one per tick
            // and share it, rather than one for every background app that's due a recheck
            using var processes = new ProcessSnapshot();

            try
            {
            // Get the default audio device
            using var localEnumerator = new MMDeviceEnumerator();
            using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                // Get the session manager for that device
                using (var sessionManager = AudioSessionManager2.FromMMDevice(device))
                using (var sessionEnumerator = sessionManager.GetSessionEnumerator())
                {
                    // Loop through the audio sessions found
                    foreach (var session in sessionEnumerator)
                    {
                        // One session going away mid-look (its app just closed) shouldn't cost us the rest of the list
                        try
                        {
                        // Dispose the raw session COM object to prevent COM handle leaks
                        using (session)
                        using (var sessionControl = session.QueryInterface<AudioSessionControl2>())
                        using (var simpleVolume = session.QueryInterface<SimpleAudioVolume>())
                        {
                            // Use ProcessID directly — no System.Diagnostics.Process allocation
                            int processId = sessionControl.ProcessID;
                            if (processId == 0)
                            {
                                // The system sounds session has no process. Note its mute state while we're here.
                                if (sessionControl.IsSystemSoundSession) systemSoundsMuted = simpleVolume.IsMuted;
                                continue;
                            }

                            // Ignore expired sessions
                            if (sessionControl.SessionState == AudioSessionState.AudioSessionStateExpired) continue;

                            _seenProcessIdsThisTick.Add(processId);

                            // FAST PATH: Use cached process info (most ticks hit this)
                            bool hitCache = false;
                            (string DisplayName, string RawProcessName, bool IsBackground, string? IconPath, DateTime LastChecked) cachedApp = default;
                            
                            hitCache = _processCache.TryGetValue(processId, out cachedApp);

                            if (hitCache)
                            {
                                // If it was previously a background app, it might have spawned a window now!
                                // Re-evaluate every 5 seconds to catch games like Blue Prince that delay their window creation.
                                if (cachedApp.IsBackground && (DateTime.UtcNow - cachedApp.LastChecked).TotalSeconds > 5)
                                {
                                    bool stillBackground = processes.IsBackground(cachedApp.RawProcessName) ?? true;
                                    _processCache[processId] = (cachedApp.DisplayName, cachedApp.RawProcessName, stillBackground, cachedApp.IconPath, DateTime.UtcNow);
                                    cachedApp.IsBackground = stillBackground; // update local tuple
                                }

                                if (_addedNames.Contains(cachedApp.DisplayName)) continue;

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

                                _addedNames.Add(cachedApp.DisplayName);
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

                            // Identify if this is a background process. If the process list can't be read,
                            // fall back to checking just this one process for a window.
                            bool isBackground = processes.IsBackground(processName) ?? HasNoWindow(process);

                            string? safeIconPath = GetExecutablePathSafely(process);

                            // Cache so we never run the slow path for this ProcessId again.
                            // We MUST do this before the _addedNames check so secondary sessions get cached 
                            // and can respond to volume/mute commands!
                            _processCache[processId] = (displayName, processName, isBackground, safeIconPath, DateTime.UtcNow);

                            // If we already have a slider for this display name, skip adding it to the UI list
                            if (_addedNames.Contains(displayName)) continue;

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

                            _addedNames.Add(displayName);
                        }
                        }
                        catch (Exception)
                        {
                            // Skip just this session and carry on with the others
                        }
                    }
                }
            }
                // Cleanup dead processes from cache (enumerating the dictionary directly avoids copying its keys every tick)
                foreach (var entry in _processCache)
                {
                    if (!_seenProcessIdsThisTick.Contains(entry.Key))
                    {
                        _processCache.TryRemove(entry.Key, out _);
                    }
                }

                // Only trust the snapshot if we actually saw the system sounds session. Otherwise
                // GetSystemSoundsMute does its own full check, exactly as it always has.
                _hasSystemSoundsSnapshot = systemSoundsMuted.HasValue;
                _systemSoundsMutedSnapshot = systemSoundsMuted ?? false;
                _systemSoundsSnapshotTicks = Environment.TickCount64;
            }
            catch (Exception)
            {
                // No audio device connected
            }

            return sessions;
            }
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

        private static bool HasNoWindow(System.Diagnostics.Process process)
        {
            try
            {
                return process.MainWindowHandle == IntPtr.Zero;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Every running process, grouped by name. Only taken the first time it's needed, then shared
        /// for the rest of the tick.
        /// </summary>
        private sealed class ProcessSnapshot : IDisposable
        {
            private System.Diagnostics.Process[]? _all;
            private ILookup<string, System.Diagnostics.Process>? _byName;
            private bool _failed;

            /// <summary>
            /// True if no process with this name has a window, which is how we tell background apps apart.
            /// Null if the process list couldn't be read.
            /// </summary>
            public bool? IsBackground(string rawProcessName)
            {
                if (_byName == null && !_failed)
                {
                    try
                    {
                        _all = System.Diagnostics.Process.GetProcesses();
                        _byName = _all.ToLookup(p => SafeName(p), StringComparer.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        _failed = true;
                    }
                }
                if (_byName == null) return null;

                string name = rawProcessName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                foreach (var p in _byName[name])
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero) return false;
                    }
                    catch { } // Ignore access denied for this one
                }
                return true;
            }

            private static string SafeName(System.Diagnostics.Process p)
            {
                try { return p.ProcessName; }
                catch { return ""; } // Exited while we looked
            }

            public void Dispose()
            {
                if (_all == null) return;
                foreach (var p in _all) p.Dispose();
            }
        }

        public void ClearCache()
        {
            _processCache.Clear();
            _deviceNameCache.Clear();
        }

        /// <summary>
        /// A device's friendly name, from our cache when we've seen it before.
        /// </summary>
        private string GetDeviceName(MMDevice device, string deviceId)
        {
            if (_deviceNameCache.TryGetValue(deviceId, out var name)) return name;

            name = device.FriendlyName;
            _deviceNameCache[deviceId] = name;
            return name;
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
            try
            {
                using var localEnumerator = new MMDeviceEnumerator();
                using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                using (var volume = AudioEndpointVolume.FromDevice(device))
                {
                    return volume.MasterVolumeLevelScalar;
                }
            }
            catch { return 0f; }
        }

        public void SetMasterVolume(float level)
        {
            Task.Run(() =>
            {
                try
                {
                    using var localEnumerator = new MMDeviceEnumerator();
                    using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    using (var volume = AudioEndpointVolume.FromDevice(device))
                    {
                        volume.MasterVolumeLevelScalar = level;
                    }
                }
                catch (Exception)
                {
                    // No output device right now (unplugged mid-drag, say)
                }
            });
        }

        public bool GetMasterMute()
        {
            try
            {
                using var localEnumerator = new MMDeviceEnumerator();
                using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                using (var volume = AudioEndpointVolume.FromDevice(device))
                {
                    return volume.IsMuted;
                }
            }
            catch { return false; }
        }

        public void SetMasterMute(bool isMuted)
        {
            Task.Run(() =>
            {
                try
                {
                    using var localEnumerator = new MMDeviceEnumerator();
                    using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    using (var volume = AudioEndpointVolume.FromDevice(device))
                    {
                        volume.IsMuted = isMuted;
                    }
                }
                catch (Exception)
                {
                    // No output device right now
                }
            });
        }

        // System Sounds Implementation

        public bool GetSystemSoundsMute()
        {
            // The polling loop asks for this straight after GetActiveAudioSessions, which has just
            // seen the system sounds session, so use that instead of walking every session again.
            lock (_pollLock)
            {
                if (_hasSystemSoundsSnapshot && Environment.TickCount64 - _systemSoundsSnapshotTicks < SystemSoundsSnapshotMaxAgeMs)
                    return _systemSoundsMutedSnapshot;
            }

            try
            {
            using var localEnumerator = new MMDeviceEnumerator();
            using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
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
            }
            catch { }
            return false;
        }

        public void SetSystemSoundsMute(bool isMuted)
        {
            Task.Run(() =>
            {
                try
                {
                    using var localEnumerator = new MMDeviceEnumerator();
                    using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
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

            try
            {
                string defaultDeviceId = string.Empty;
                using var localEnumerator = new MMDeviceEnumerator();
                using (var defaultDevice = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    if (defaultDevice != null)
                    {
                        defaultDeviceId = defaultDevice.DeviceID;
                    }
                }
                
                using (var devices = localEnumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active))
                {
                    foreach (var device in devices)
                    {
                        string deviceId = device.DeviceID;
                        result.Add(new AudioDeviceModel
                        {
                            Id = deviceId,
                            Name = GetDeviceName(device, deviceId),
                            IsDefault = deviceId == defaultDeviceId
                        });
                        device.Dispose(); // Dispose each individual device after processing
                    }
                }
            }
            catch { /* No audio device available */ }

            return result;
        }

        public void SetDefaultAudioDevice(string deviceId)
        {
            AudioDeviceSwitcher.SetDefaultDevice(deviceId);
        }

        // --- Input Device (Microphone) Implementation ---

        public List<AudioDeviceModel> GetInputDevices()
        {
            var result = new List<AudioDeviceModel>();

            try
            {
                string defaultDeviceId = string.Empty;
                using var localEnumerator = new MMDeviceEnumerator();
                try
                {
                    using (var defaultDevice = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
                    {
                        if (defaultDevice != null)
                        {
                            defaultDeviceId = defaultDevice.DeviceID;
                        }
                    }
                }
                catch { /* No default capture device */ }

                using (var devices = localEnumerator.EnumAudioEndpoints(DataFlow.Capture, DeviceState.Active))
                {
                    foreach (var device in devices)
                    {
                        string deviceId = device.DeviceID;
                        result.Add(new AudioDeviceModel
                        {
                            Id = deviceId,
                            Name = GetDeviceName(device, deviceId),
                            IsDefault = deviceId == defaultDeviceId
                        });
                        device.Dispose();
                    }
                }
            }
            catch { /* No input device available */ }

            return result;
        }

        public void SetDefaultInputDevice(string deviceId)
        {
            // The SetDefaultEndpoint COM call is device-type agnostic — it works for both
            // render (output) and capture (input) devices based on the device ID.
            AudioDeviceSwitcher.SetDefaultDevice(deviceId);
        }

        public bool GetInputMute()
        {
            try
            {
                using var localEnumerator = new MMDeviceEnumerator();
                using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
                using (var volume = AudioEndpointVolume.FromDevice(device))
                {
                    return volume.IsMuted;
                }
            }
            catch { return false; }
        }

        public void SetInputMute(bool isMuted)
        {
            // A fresh enumerator each time, like everywhere else. This gets called from the UI thread and from
            // the companion server's threads, and a shared one made on the UI thread isn't safe to use from both.
            try
            {
                using var localEnumerator = new MMDeviceEnumerator();
                using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
                using (var volume = AudioEndpointVolume.FromDevice(device))
                {
                    volume.IsMuted = isMuted;
                }
            }
            catch { }
        }

        public float GetInputVolume()
        {
            try
            {
                using var localEnumerator = new MMDeviceEnumerator();
                using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
                using (var volume = AudioEndpointVolume.FromDevice(device))
                {
                    return volume.MasterVolumeLevelScalar;
                }
            }
            catch { return 0f; }
        }

        public void Dispose()
        {
            _processCache.Clear();
            _deviceNameCache.Clear();
        }

        private void PerformActionOnSession(string targetProcessName, Action<SimpleAudioVolume> action)
        {
            Task.Run(() =>
            {
                try
                {
                    using var localEnumerator = new MMDeviceEnumerator();
                    using (var device = localEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    using (var sessionManager = AudioSessionManager2.FromMMDevice(device))
                    using (var sessionEnumerator = sessionManager.GetSessionEnumerator())
                    {
                        foreach (var session in sessionEnumerator)
                        {
                            // A multi-process app (Chrome, Discord) has several sessions, so one failing
                            // mustn't stop the rest of them getting the change
                            try
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
                            catch (Exception)
                            {
                                // That session just went away, carry on with the others
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

        // Unicode, so paths with accents or non-Latin characters (e.g. in a user's name) come back intact
        [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hHandle);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private static string? GetExecutablePathSafely(System.Diagnostics.Process process)
        {
            // Asking Windows for the image name directly is far cheaper than Process.MainModule (which reads
            // the whole module list), and it also works on anti-cheat protected games like Valorant
            try
            {
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
            }
            catch (InvalidOperationException)
            {
                // Process just exited
                return null;
            }

            // Last resort
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }
    }
}