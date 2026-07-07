using System;
using System.Runtime.InteropServices;

namespace SoundBar.Helpers
{
    /// <summary>
    /// A sneaky little helper class that fakes pressing media keys on the keyboard.
    /// It's a remarkably effective way to control background music players like Spotify.
    /// </summary>
    public static class MediaHelper
    {
        // We import the ancient user32.dll from Windows to send fake key presses.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const int KEYEVENTF_KEYUP = 0x0002;

        // Magic numbers for media keys
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_MEDIA_STOP = 0xB2;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const byte VK_VOLUME_MUTE = 0xAD;

        /// <summary>
        /// Pretends to press the Play/Pause button on the keyboard.
        /// </summary>
        public static void PlayPause()
        {
            SendMediaKey(VK_MEDIA_PLAY_PAUSE);
        }

        /// <summary>
        /// Pretends to press the Next Track button.
        /// </summary>
        public static void NextTrack()
        {
            SendMediaKey(VK_MEDIA_NEXT_TRACK);
        }

        /// <summary>
        /// Pretends to press the Previous Track button.
        /// </summary>
        public static void PreviousTrack()
        {
            SendMediaKey(VK_MEDIA_PREV_TRACK);
        }

        /// <summary>
        /// Pretends to press the mute button on the keyboard.
        /// </summary>
        public static void Mute()
        {
            SendMediaKey(VK_VOLUME_MUTE);
        }

        /// <summary>
        /// Does the actual dirty work of sending the key press and release to Windows.
        /// </summary>
        /// <param name="key">The byte code of the key to press.</param>
        private static void SendMediaKey(byte key)
        {
            // First we push the key down...
            keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            
            // ...and then we lift our finger off it.
            keybd_event(key, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }
}
