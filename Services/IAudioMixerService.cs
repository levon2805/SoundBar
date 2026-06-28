using SoundBar.Models;

namespace SoundBar.Services
{
    public interface IAudioMixerService
    {
        // Returns list of all apps currently making sound
        List<AudioAppModel> GetActiveAudioSessions(IEnumerable<string>? knownAppNames = null);

        // Update volume for specific app using Process Name
        void SetVolume(string processName, float level);

        // Mutes or unmutes specific app
        void SetMute(string processName, bool isMuted);

        // Master volume controls
        float GetMasterVolume();
        void SetMasterVolume(float level);
        bool GetMasterMute();
        void SetMasterMute(bool isMuted);
    }
}