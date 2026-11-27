using System;
using System.Runtime.InteropServices;
using System.Threading;
using CSCore.CoreAudioAPI;

namespace SoundBar.Services
{
    /// <summary>
    /// Listens for Windows saying something changed, so SoundBar can look straight away instead of waiting for
    /// its next regular check. That lets the regular checks be much less frequent, which saves CPU.
    /// </summary>
    /// <remarks>
    /// It watches for:
    /// - output or input devices being added, removed or made the default;
    /// - the master volume or mute, and the microphone mute, changing (from the keyboard or Windows' flyout, say);
    /// - an app starting to play sound;
    /// - a different window coming to the front (for the active app highlight).
    ///
    /// Each just calls the "check now" action. The regular check still runs as a safety net, so a missed
    /// notification only means a short delay, never stale information.
    ///
    /// Everything is set up and released on one background thread, which also receives the window notification.
    /// </remarks>
    internal sealed class AudioChangeWatcher : IDisposable
    {
        private const uint WM_QUIT = 0x0012;
        private const uint WM_APP_REOPEN = 0x8001; // The default device changed: watch the new one
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private readonly Action _changed;
        private readonly Thread _thread;
        private uint _threadId;

        // Only touched on the watcher's thread
        private MMDeviceEnumerator? _enumerator;
        private Watched? _output;
        private Watched? _input;
        private AudioSessionManager2? _sessions;
        private IntPtr _foregroundHook;
        private WinEventProc? _foregroundProc; // Kept alive while Windows can call it

        /// <param name="changed">Called (on any thread) whenever something changed. Keep it quick.</param>
        public AudioChangeWatcher(Action changed)
        {
            _changed = changed;
            _thread = new Thread(Run) { IsBackground = true, Name = "SoundBar change watcher" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private void Notify()
        {
            try { _changed(); } catch { }
        }

        private void Run()
        {
            _threadId = GetCurrentThreadId();
            try
            {
                _enumerator = new MMDeviceEnumerator();
                _enumerator.DefaultDeviceChanged += Enumerator_DefaultDeviceChanged;
                _enumerator.DeviceAdded += Enumerator_DevicesChanged;
                _enumerator.DeviceRemoved += Enumerator_DevicesChanged;
                _enumerator.DeviceStateChanged += Enumerator_DevicesChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Change watcher: no device notifications ({ex.Message})");
            }
            Reopen();

            _foregroundProc = (_, _, _, _, _, _, _) => Notify();
            _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);

            // The foreground notification is delivered through this thread's messages
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.hwnd == IntPtr.Zero && msg.message == WM_APP_REOPEN)
                {
                    Reopen();
                    continue;
                }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            // Shutting down: let go of everything on the thread that made it
            if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
            Release();
            if (_enumerator != null)
            {
                try { _enumerator.DefaultDeviceChanged -= Enumerator_DefaultDeviceChanged; } catch { }
                try { _enumerator.DeviceAdded -= Enumerator_DevicesChanged; } catch { }
                try { _enumerator.DeviceRemoved -= Enumerator_DevicesChanged; } catch { }
                try { _enumerator.DeviceStateChanged -= Enumerator_DevicesChanged; } catch { }
                try { _enumerator.Dispose(); } catch { }
                _enumerator = null;
            }
            GC.KeepAlive(_foregroundProc);
        }

        private void Enumerator_DefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
        {
            // Arrives on Windows' own thread: move the re-subscribing to ours
            if (_threadId != 0) PostThreadMessage(_threadId, WM_APP_REOPEN, IntPtr.Zero, IntPtr.Zero);
            Notify();
        }

        private void Enumerator_DevicesChanged(object? sender, EventArgs e) => Notify();

        /// <summary>Watches the current default speakers and microphone (again), and the apps playing on the speakers.</summary>
        private void Reopen()
        {
            Release();
            if (_enumerator == null) return;

            _output = Watched.Open(_enumerator, DataFlow.Render, Notify);
            _input = Watched.Open(_enumerator, DataFlow.Capture, Notify);

            try
            {
                if (_output != null)
                {
                    _sessions = AudioSessionManager2.FromMMDevice(_output.Device);
                    _sessions.SessionCreated += Sessions_SessionCreated;
                    // Windows only starts announcing new sessions once the list has been read
                    using (_sessions.GetSessionEnumerator()) { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Change watcher: no new-app notifications ({ex.Message})");
            }
        }

        private void Sessions_SessionCreated(object? sender, SessionCreatedEventArgs e)
        {
            try { e.NewSession?.Dispose(); } catch { }
            Notify();
        }

        private void Release()
        {
            if (_sessions != null)
            {
                try { _sessions.SessionCreated -= Sessions_SessionCreated; } catch { }
                try { _sessions.Dispose(); } catch { }
                _sessions = null;
            }
            _output?.Dispose();
            _output = null;
            _input?.Dispose();
            _input = null;
        }

        public void Dispose()
        {
            if (_threadId != 0 && PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero))
            {
                // Don't wait forever if Windows' audio service is stuck; the app is closing anyway
                _thread.Join(TimeSpan.FromSeconds(3));
            }
            _threadId = 0;
        }

        /// <summary>One default device, with its volume and mute notifications.</summary>
        private sealed class Watched : IDisposable
        {
            public MMDevice Device { get; }
            private readonly AudioEndpointVolume _volume;
            private readonly AudioEndpointVolumeCallback _callback;
            private readonly EventHandler<AudioEndpointVolumeCallbackEventArgs> _handler;

            private Watched(MMDevice device, AudioEndpointVolume volume, Action notify)
            {
                Device = device;
                _volume = volume;
                _callback = new AudioEndpointVolumeCallback();
                _handler = (_, _) => notify();
                _callback.NotifyRecived += _handler;
                _volume.RegisterControlChangeNotify(_callback);
            }

            /// <summary>The default device for this direction, or null if there isn't one (no microphone, say).</summary>
            public static Watched? Open(MMDeviceEnumerator enumerator, DataFlow flow, Action notify)
            {
                const int E_NOTFOUND = unchecked((int)0x80070490);
                MMDevice? device = null;
                AudioEndpointVolume? volume = null;
                try
                {
                    int hr = enumerator.GetDefaultAudioEndpointNative(flow, Role.Multimedia, out IntPtr pointer);
                    if (hr == E_NOTFOUND || pointer == IntPtr.Zero) return null;
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                    device = new MMDevice(pointer);
                    volume = AudioEndpointVolume.FromDevice(device);
                    return new Watched(device, volume, notify);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Change watcher: couldn't watch the default {flow} device ({ex.Message})");
                    volume?.Dispose();
                    device?.Dispose();
                    return null;
                }
            }

            public void Dispose()
            {
                try { _volume.UnregisterControlChangeNotify(_callback); } catch { }
                _callback.NotifyRecived -= _handler;
                try { _volume.Dispose(); } catch { }
                try { Device.Dispose(); } catch { }
            }
        }

        // --- Windows API ---

        private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
