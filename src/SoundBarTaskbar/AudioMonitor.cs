using System.Collections.Concurrent;
using CSCore.CoreAudioAPI;
using CSCore.Win32;

namespace SoundBarTaskbar
{
    /// <summary>What the indicator shows. A new one is made whenever anything changes; it's never edited.</summary>
    internal sealed record AudioState(
        bool HasOutput, string OutputName, int Volume, bool OutputMuted, bool IsHeadphones,
        bool HasMic, string MicName, bool MicMuted)
    {
        public static readonly AudioState Empty = new(false, "", 0, false, false, false, "", false);
    }

    /// <summary>
    /// Watches the default speakers/headphones and microphone, and changes their volume and mute.
    ///
    /// Everything runs on one background thread. The window thread shares its input with Explorer's taskbar
    /// (our window lives inside it), so it must never wait on Windows' audio service: a slow call there would
    /// freeze the user's taskbar. Windows tells us about changes through callbacks, so nothing is polled.
    /// </summary>
    internal sealed class AudioMonitor : IDisposable
    {
        // Same role SoundBar uses, so both always agree on which device is "the" default.
        private const Role DeviceRole = Role.Multimedia;

        // {1da5d803-d492-4edd-8c23-e0c0ffee7f0e},0: what kind of device it is (headphones, speakers...).
        private static readonly PropertyKey FormFactorKey = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
        private const int FormFactorHeadphones = 3;
        private const int FormFactorHeadset = 5;

        private readonly BlockingCollection<Action> _work = new();
        private readonly Thread _thread;
        private readonly Action _changed;

        // Only touched on the audio thread
        private MMDeviceEnumerator? _enumerator;
        private Endpoint? _output;
        private Endpoint? _mic;
        private bool _refreshQueued;
        private int _refreshFailures;

        private volatile AudioState _state = AudioState.Empty;
        private int _refreshRequested;

        /// <summary>The latest state. Safe to read from any thread.</summary>
        public AudioState State => _state;

        /// <param name="changed">Called on the audio thread whenever <see cref="State"/> changes. Keep it quick.</param>
        public AudioMonitor(Action changed)
        {
            _changed = changed;
            _thread = new Thread(Run) { IsBackground = true, Name = "SoundBarTaskbar audio" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
            Post(Refresh);
        }

        public void ToggleOutputMute() => Post(() => _output?.SetMute(!_output.Volume.IsMuted));

        public void ToggleMicMute() => Post(() => _mic?.SetMute(!_mic.Volume.IsMuted));

        /// <summary>Nudges the volume up or down by a number of percentage points.</summary>
        public void ChangeVolume(int percentPoints) => Post(() =>
        {
            if (_output == null) return;
            float level = _output.Volume.GetMasterVolumeLevelScalar();
            // Snap to whole steps, like Windows' own volume flyout does
            float target = Math.Clamp((float)Math.Round(level * 100 + percentPoints) / 100f, 0f, 1f);
            _output.Volume.SetMasterVolumeLevelScalar(target, Guid.Empty);
        });

        /// <summary>
        /// Looks at the devices again. Used when Windows says they changed, and as a retry if the audio service
        /// wasn't ready yet (for example straight after signing in). Safe to call from any thread.
        /// </summary>
        public void RequestRefresh()
        {
            if (Interlocked.Exchange(ref _refreshRequested, 1) == 0)
                Post(() => { Interlocked.Exchange(ref _refreshRequested, 0); Refresh(); });
        }

        /// <summary>True if the last attempt to read the devices failed, so it's worth trying again later.</summary>
        public bool NeedsRetry => Volatile.Read(ref _refreshFailures) > 0;

        private void Post(Action action)
        {
            // Already shutting down. (A Windows notification can arrive on its own thread at any moment, and an
            // exception there would end the program. This also covers ObjectDisposedException, a kind of
            // InvalidOperationException.)
            try { _work.Add(action); } catch (InvalidOperationException) { }
        }

        private void Run()
        {
            foreach (var action in _work.GetConsumingEnumerable())
            {
                try { action(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Audio: {ex.Message}"); }
            }

            // Shutting down: let go of every Windows audio object on the thread that made them
            ReleaseEndpoints();
            if (_enumerator != null)
            {
                try { _enumerator.DefaultDeviceChanged -= Enumerator_DefaultDeviceChanged; } catch { }
                try { _enumerator.DeviceStateChanged -= Enumerator_DevicesChanged; } catch { }
                try { _enumerator.DeviceAdded -= Enumerator_DevicesChanged; } catch { }
                try { _enumerator.DeviceRemoved -= Enumerator_DevicesChanged; } catch { }
                try { _enumerator.Dispose(); } catch { }
                _enumerator = null;
            }
        }

        private void Refresh()
        {
            _refreshQueued = false;
            try
            {
                if (_enumerator == null)
                {
                    _enumerator = new MMDeviceEnumerator();
                    _enumerator.DefaultDeviceChanged += Enumerator_DefaultDeviceChanged;
                    _enumerator.DeviceStateChanged += Enumerator_DevicesChanged;
                    _enumerator.DeviceAdded += Enumerator_DevicesChanged;
                    _enumerator.DeviceRemoved += Enumerator_DevicesChanged;
                }

                ReleaseEndpoints();
                _output = Endpoint.Open(_enumerator, DataFlow.Render, OnVolumeNotification);
                _mic = Endpoint.Open(_enumerator, DataFlow.Capture, OnVolumeNotification);
                Volatile.Write(ref _refreshFailures, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't read the audio devices: {ex.Message}");
                ReleaseEndpoints();
                Volatile.Write(ref _refreshFailures, _refreshFailures + 1);
            }
            Publish();
        }

        private void ReleaseEndpoints()
        {
            _output?.Dispose();
            _mic?.Dispose();
            _output = null;
            _mic = null;
        }

        private void Publish()
        {
            AudioState next;
            try
            {
                next = new AudioState(
                    _output != null, _output?.Name ?? "",
                    _output != null ? (int)Math.Round(_output.Volume.GetMasterVolumeLevelScalar() * 100) : 0,
                    _output?.Volume.IsMuted ?? false, _output?.IsHeadphones ?? false,
                    _mic != null, _mic?.Name ?? "", _mic?.Volume.IsMuted ?? false);
            }
            catch (Exception ex)
            {
                // The device vanished between the callback and now. Windows will tell us about the new one.
                System.Diagnostics.Debug.WriteLine($"Couldn't read the volume: {ex.Message}");
                QueueRefresh();
                return;
            }

            if (next != _state)
            {
                _state = next;
                _changed();
            }
        }

        private void QueueRefresh()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            Post(Refresh);
        }

        // These arrive on Windows' threads. Never touch the devices here: hand the work to our own thread.
        private void OnVolumeNotification() => Post(Publish);

        private void Enumerator_DefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
        {
            if (e.Role == DeviceRole) RequestRefresh();
        }

        private void Enumerator_DevicesChanged(object? sender, EventArgs e) => RequestRefresh();

        public void Dispose()
        {
            _work.CompleteAdding();
            // The audio thread releases everything itself. Don't wait forever if the audio service is stuck.
            if (_thread.Join(TimeSpan.FromSeconds(3)))
                _work.Dispose();
            else
                System.Diagnostics.Debug.WriteLine("The audio thread didn't stop in time."); // The process is exiting anyway
        }

        /// <summary>One default device, with its volume control and change notifications.</summary>
        private sealed class Endpoint : IDisposable
        {
            private readonly MMDevice _device;
            private readonly AudioEndpointVolumeCallback _callback;
            private readonly EventHandler<AudioEndpointVolumeCallbackEventArgs> _handler;

            public AudioEndpointVolume Volume { get; }
            public string Name { get; }
            public bool IsHeadphones { get; }

            private Endpoint(MMDevice device, AudioEndpointVolume volume, string name, bool isHeadphones, Action notify)
            {
                _device = device;
                Volume = volume;
                Name = name;
                IsHeadphones = isHeadphones;
                _callback = new AudioEndpointVolumeCallback();
                _handler = (_, _) => notify();
                _callback.NotifyRecived += _handler;
                Volume.RegisterControlChangeNotify(_callback);
            }

            /// <summary>The default device for this direction, or null if there isn't one.</summary>
            public static Endpoint? Open(MMDeviceEnumerator enumerator, DataFlow flow, Action notify)
            {
                // E_NOTFOUND means there's simply no such device (no microphone plugged in, say)
                const int E_NOTFOUND = unchecked((int)0x80070490);
                IntPtr pointer;
                int hr = enumerator.GetDefaultAudioEndpointNative(flow, DeviceRole, out pointer);
                if (hr == E_NOTFOUND || pointer == IntPtr.Zero) return null;
                if (hr < 0) System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hr);
                var device = new MMDevice(pointer);

                AudioEndpointVolume? volume = null;
                try
                {
                    string name = device.FriendlyName ?? "";
                    bool headphones = false;
                    if (flow == DataFlow.Render)
                    {
                        try
                        {
                            using var store = device.OpenPropertyStore(StorageAccess.Read);
                            using var value = store.GetValue(FormFactorKey);
                            int formFactor = value.DataType == System.Runtime.InteropServices.VarEnum.VT_UI4 ? (int)value.UIntValue : -1;
                            headphones = formFactor == FormFactorHeadphones || formFactor == FormFactorHeadset;
                        }
                        catch { }
                    }

                    volume = AudioEndpointVolume.FromDevice(device);
                    return new Endpoint(device, volume, name, headphones, notify);
                }
                catch
                {
                    volume?.Dispose();
                    device.Dispose();
                    throw;
                }
            }

            public void SetMute(bool muted) => Volume.SetMute(muted, Guid.Empty);

            public void Dispose()
            {
                try { Volume.UnregisterControlChangeNotify(_callback); } catch { }
                _callback.NotifyRecived -= _handler;
                try { Volume.Dispose(); } catch { }
                try { _device.Dispose(); } catch { }
            }
        }
    }
}
