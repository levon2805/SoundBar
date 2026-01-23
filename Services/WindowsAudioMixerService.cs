using CSCore.CoreAudioAPI;
using SoundBar.Models;
using System;
using System.Collections.Generic;
using System.Data;

namespace SoundBar.Services
{
    internal class WindowsAudioMixerService : IAudioMixerService
    {
        public List<AudioAppModel> GetActiveAudioSessions()
        {
            var apps = new List<AudioAppModel>();

            // Get the default audio device
            using (var enumerator = new MMDeviceEnumerator())
            using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                // Get the session manager for that device
                // The manager knows about every app playing sound on that device
                using (var sessionManager = AudioSessionManager2.FromMMDevice(device))
                using (var sessionEnumerator = sessionManager.GetSessionEnumerator())
                {
                    // Loop through the audio sessions found
                    foreach (var session in sessionEnumerator)
                    {
                        // QueryInterface to get controls
                        // AudioSessionControl2 gives process information
                        // SimpleAudiovolume gives volume controls
                        using (var sessionControl = session.QueryInterface<AudioSessionControl2>())
                        using (var simpleVolume = session.QueryInterface<SimpleAudioVolume>())
                        {
                            var process = sessionControl.Process;
                            // Some system sounds don't have PID
                            if (process == null) continue;

                            // Try to get icon path, if access denied catch error and set to null
                            string? safeIconPath = null;
                            try
                            {
                                safeIconPath = process.MainModule?.FileName;
                            }
                            catch (System.ComponentModel.Win32Exception)
                            {
                                // Ignore and keep going
                            }

                            apps.Add(new AudioAppModel
                            {
                                ProcessId = process.Id,
                                Name = process.ProcessName,
                                Volume = simpleVolume.MasterVolume,
                                IsMuted = simpleVolume.IsMuted,
                                IconPath = safeIconPath
                            });
                        }
                    }
                }
            }
            return apps;
        }

        public void SetVolume(int processId, float level)
        {
            // Reuse helper method for finding specific session and applying changes
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

        // Helper method as SetVolume and SetMute need to find the session first
        private void PerformActionOnSession(int targetProcessId, Action<SimpleAudioVolume> action)
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
                        // Check if correct app its looking for
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
    }
}