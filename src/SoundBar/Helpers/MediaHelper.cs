using System;
using System.Runtime.InteropServices;

namespace SoundBar.Helpers
{
    public static class MediaHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const int KEYEVENTF_KEYUP = 0x0002;

        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_MEDIA_STOP = 0xB2;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const byte VK_VOLUME_MUTE = 0xAD;

        public static void PlayPause()
        {
            SendMediaKey(VK_MEDIA_PLAY_PAUSE);
        }

        public static void NextTrack()
        {
            SendMediaKey(VK_MEDIA_NEXT_TRACK);
        }

        public static void PreviousTrack()
        {
            SendMediaKey(VK_MEDIA_PREV_TRACK);
        }

        public static void Mute()
        {
            SendMediaKey(VK_VOLUME_MUTE);
        }

        private static void SendMediaKey(byte key)
        {
            // Press the key down
            keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            // Release the key
            keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }
}
