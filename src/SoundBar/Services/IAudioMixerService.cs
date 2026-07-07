using SoundBar.Models;

namespace SoundBar.Services
{
    /// <summary>
    /// The grand contract for anything that wants to boss around the system audio.
    /// Implement this to control volumes, mutes, and devices.
    /// </summary>
    public interface IAudioMixerService
    {
        /// <summary>
        /// Grabs a quick, lightweight snapshot of all the applications currently making a racket.
        /// Does not cause any side effects or alter volumes.
        /// </summary>
        List<AudioSessionData> GetActiveAudioSessions();

        /// <summary>
        /// Shoves the volume of a specific application up or down.
        /// </summary>
        /// <param name="processName">The raw executable name (like 'spotify.exe').</param>
        /// <param name="level">The new volume level, from 0.0 to 1.0.</param>
        void SetVolume(string processName, float level);

        /// <summary>
        /// Instantly gags (or ungags) a specific application.
        /// </summary>
        /// <param name="processName">The raw executable name.</param>
        /// <param name="isMuted">True to mute, false to let the music play.</param>
        void SetMute(string processName, bool isMuted);

        /// <summary>
        /// Fetches the overall master volume for the entire computer.
        /// </summary>
        float GetMasterVolume();

        /// <summary>
        /// Sets the master volume for the entire system. Don't blow the speakers!
        /// </summary>
        /// <param name="level">The new volume level, from 0.0 to 1.0.</param>
        void SetMasterVolume(float level);

        /// <summary>
        /// Checks if the entire system is currently muted.
        /// </summary>
        bool GetMasterMute();

        /// <summary>
        /// Mutes or unmutes the entire system in one go.
        /// </summary>
        void SetMasterMute(bool isMuted);

        /// <summary>
        /// Checks if those pesky Windows system sounds (like error dings) are muted.
        /// </summary>
        bool GetSystemSoundsMute();

        /// <summary>
        /// Silences (or enables) system sounds. A blessing for deep work.
        /// </summary>
        void SetSystemSoundsMute(bool isMuted);

        /// <summary>
        /// Asks Windows for a list of all the speakers and headphones plugged in right now.
        /// </summary>
        List<AudioDeviceModel> GetAudioDevices();

        /// <summary>
        /// Tells Windows to switch all audio over to a different playback device.
        /// </summary>
        /// <param name="deviceId">The unique system identifier for the device.</param>
        void SetDefaultAudioDevice(string deviceId);
    }
}