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

            // Track which IDs we have processed to prevent duplicates
            var addedProcessIds = new HashSet<int>();

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

                            // Ignore Inactive sessions
                            if (sessionControl.SessionState != AudioSessionState.AudioSessionStateActive) continue;

                            // If we already have a slider for this App ID, skip it to prevent duplicates
                            if (addedProcessIds.Contains(process.Id)) continue;

                            string? safeIconPath = null;
                            try
                            {
                                safeIconPath = process.MainModule?.FileName;
                            }
                            catch (System.ComponentModel.Win32Exception)
                            {
                                // Ignore and keep going
                            }

                            var newApp = new AudioAppModel(this)
                            {
                                ProcessId = process.Id,
                                Name = process.ProcessName,
                                Volume = simpleVolume.MasterVolume,
                                IsMuted = simpleVolume.IsMuted,
                                IconPath = safeIconPath
                            };

                            apps.Add(newApp);
                            addedProcessIds.Add(process.Id);
                        }
                    }
                }
            }
            return apps;
        }

        public void SetVolume(int processId, float level)
        {
            PerformActionOnSession(processId, (volumeControl) =>
            {
                volumeControl.MasterVolume = level;
            });
        }

        public void SetMute(int processId, bool isMuted)
        {
            PerformActionOnSession(processId, (volumeControl) =>
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

        private void PerformActionOnSession(int targetProcessId, Action<SimpleAudioVolume> action)
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
                                if (sessionControl.Process != null && sessionControl.Process.Id == targetProcessId)
                                {
                                    // If yes, execute
                                    action(simpleVolume);
                                    return; // Stop searching
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
    }
}