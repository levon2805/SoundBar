using SoundBar.Models;

namespace SoundBar.Services
{
    public interface IAudioMixerService
    {
        // Returns list of all apps currently making sound
        List<AudioAppModel> GetActiveAudioSessions();

        // Update volume for specific app using PID
        void SetVolume(int processID, float level);

        // Mutes or unmutes specific app
        void SetMute(int processID, bool isMuted);

        // Master volume controls
        float GetMasterVolume();
        void SetMasterVolume(float level);
        bool GetMasterMute();
        void SetMasterMute(bool isMuted);
    }
}