using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SoundBar.Helpers;
using SoundBar.Models;
using SoundBar.Services;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace SoundBar.ViewModels
{
    /// <summary>
    /// The kinds of question the Bluetooth page can ask while pairing.
    /// </summary>
    public enum BluetoothPromptKind
    {
        None,
        /// <summary>Just telling the user something, like a PIN to type on the device.</summary>
        Info,
        /// <summary>"Does this PIN match the one on your device?"</summary>
        Confirm,
        /// <summary>"Type the PIN shown on your device."</summary>
        EnterPin
    }

    /// <summary>
    /// Everything behind the Bluetooth page: turning Bluetooth on and off, the paired devices list,
    /// connecting speakers and headphones, and finding and pairing new devices.
    ///
    /// Only exists while the page is open. Closing the page disposes it, which stops every Windows device
    /// watcher it started, so it costs nothing the rest of the time.
    /// </summary>
    public sealed class BluetoothViewModel : INotifyPropertyChanged, IDisposable
    {
        private const string ContainerIdProperty = "System.Devices.Aep.ContainerId";
        private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
        private const string ClassMajorProperty = "System.Devices.Aep.Bluetooth.Cod.Major";
        private const string ClassMinorProperty = "System.Devices.Aep.Bluetooth.Cod.Minor";
        private const string LowEnergyAppearanceProperty = "System.Devices.Aep.Bluetooth.Le.Appearance";

        private static readonly string[] RequestedProperties =
        {
            ContainerIdProperty, IsConnectedProperty, ClassMajorProperty, ClassMinorProperty, LowEnergyAppearanceProperty
        };

        /// <summary>
        /// Every pairing style we can walk the user through on this page.
        /// </summary>
        private const DevicePairingKinds SupportedPairingKinds =
            DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.ProvidePin;

        /// <summary>
        /// How long a device gets to connect or disconnect before we say something went wrong.
        /// </summary>
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How long we wait for a newly connected speaker's sound output to be ready before giving up on switching to it.
        /// </summary>
        private static readonly TimeSpan OutputReadyTimeout = TimeSpan.FromSeconds(6);

        private readonly DispatcherQueue? _dispatcher;
        private readonly List<DeviceWatcher> _pairedWatchers = new();
        private readonly List<DeviceWatcher> _scanWatchers = new();
        private readonly Dictionary<string, DeviceInformation> _pairedEntries = new();
        private readonly Dictionary<string, DeviceInformation> _nearbyEntries = new();

        private Radio? _radio;
        private bool _canControlRadio;
        private bool _hasChecked;
        private int _pairedListsLoaded;
        private TaskCompletionSource<string?>? _prompt;
        private bool _isDisposed;

        public BluetoothViewModel(DispatcherQueue? dispatcher)
        {
            _dispatcher = dispatcher;
        }

        /// <summary>
        /// Devices already paired with this PC, connected ones first.
        /// </summary>
        public ObservableCollection<BluetoothDeviceItem> PairedDevices { get; } = new();

        /// <summary>
        /// Devices nearby that can be paired, filled in while searching.
        /// </summary>
        public ObservableCollection<BluetoothDeviceItem> NearbyDevices { get; } = new();

        // --- Page state ---

        private bool HasBluetooth => _radio != null;

        public Visibility AvailableVisibility => _hasChecked && HasBluetooth ? Visibility.Visible : Visibility.Collapsed;

        public Visibility UnavailableVisibility => _hasChecked && !HasBluetooth ? Visibility.Visible : Visibility.Collapsed;

        public Visibility LoadingVisibility => _hasChecked ? Visibility.Collapsed : Visibility.Visible;

        private bool _isRadioOn;
        public bool IsRadioOn
        {
            get => _isRadioOn;
            private set
            {
                if (_isRadioOn == value) return;
                _isRadioOn = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanScan));
                foreach (var item in PairedDevices) item.IsRadioOn = value;
                foreach (var item in NearbyDevices) item.IsRadioOn = value;
            }
        }

        private bool _isRadioBusy;
        public bool CanToggleRadio => HasBluetooth && _canControlRadio && !_isRadioBusy;

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            private set
            {
                if (_isScanning == value) return;
                _isScanning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScanButtonText));
                OnPropertyChanged(nameof(ScanningVisibility));
            }
        }

        public bool CanScan => IsRadioOn;

        public string ScanButtonText => IsScanning ? "Stop" : "Search";

        public Visibility ScanningVisibility => IsScanning ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// "No paired devices yet", shown once Windows has finished listing them and there weren't any.
        /// </summary>
        public Visibility NoPairedDevicesVisibility =>
            _pairedListsLoaded >= _pairedWatchers.Count && _pairedWatchers.Count > 0 && PairedDevices.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        private string? _message;
        /// <summary>
        /// Something that went wrong, shown in a bar at the top of the page.
        /// </summary>
        public string? Message
        {
            get => _message;
            private set
            {
                if (_message == value) return;
                _message = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMessage));
            }
        }

        public bool HasMessage => !string.IsNullOrEmpty(Message);

        public void ClearMessage() => Message = null;

        // --- Pairing prompt ---

        private BluetoothPromptKind _promptKind;
        public BluetoothPromptKind PromptKind
        {
            get => _promptKind;
            private set
            {
                if (_promptKind == value) return;
                _promptKind = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PromptVisibility));
                OnPropertyChanged(nameof(PromptButtonsVisibility));
                OnPropertyChanged(nameof(PinEntryVisibility));
                OnPropertyChanged(nameof(PromptAcceptText));
            }
        }

        private string _promptText = string.Empty;
        public string PromptText
        {
            get => _promptText;
            private set
            {
                if (_promptText == value) return;
                _promptText = value;
                OnPropertyChanged();
            }
        }

        public Visibility PromptVisibility => PromptKind != BluetoothPromptKind.None ? Visibility.Visible : Visibility.Collapsed;

        public Visibility PromptButtonsVisibility =>
            PromptKind is BluetoothPromptKind.Confirm or BluetoothPromptKind.EnterPin ? Visibility.Visible : Visibility.Collapsed;

        public Visibility PinEntryVisibility => PromptKind == BluetoothPromptKind.EnterPin ? Visibility.Visible : Visibility.Collapsed;

        public string PromptAcceptText => PromptKind == BluetoothPromptKind.Confirm ? "Yes, they match" : "Pair";

        /// <summary>
        /// The user answered the pairing question. A PIN for "enter PIN", anything for "yes", null for cancel.
        /// </summary>
        public void AnswerPrompt(bool accepted, string? pin = null)
        {
            var prompt = _prompt;
            if (prompt == null) return;

            if (!accepted)
            {
                ClosePrompt(prompt, null);
                return;
            }

            if (PromptKind == BluetoothPromptKind.EnterPin)
            {
                pin = pin?.Trim();
                if (string.IsNullOrEmpty(pin)) return; // Leave the box open until they type something
                ClosePrompt(prompt, pin);
            }
            else
            {
                ClosePrompt(prompt, "yes");
            }
        }

        private void ClosePrompt(TaskCompletionSource<string?> prompt, string? answer)
        {
            if (ReferenceEquals(_prompt, prompt))
            {
                _prompt = null;
                PromptKind = BluetoothPromptKind.None;
                PromptText = string.Empty;
            }
            prompt.TrySetResult(answer);
        }

        private void ShowInfo(string text)
        {
            // An info line never waits for an answer, but it replaces any question still showing
            _prompt?.TrySetResult(null);
            _prompt = null;
            PromptText = text;
            PromptKind = BluetoothPromptKind.Info;
        }

        private void HidePrompt()
        {
            var prompt = _prompt;
            _prompt = null;
            PromptKind = BluetoothPromptKind.None;
            PromptText = string.Empty;
            prompt?.TrySetResult(null);
        }

        /// <summary>
        /// Shows a question on the page and waits for the answer. Safe to call from any thread.
        /// </summary>
        private Task<string?> AskAsync(BluetoothPromptKind kind, string text)
        {
            var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            bool queued = RunOnUIThread(() =>
            {
                if (_isDisposed)
                {
                    answer.TrySetResult(null);
                    return;
                }

                // Only one question at a time; a newer one cancels the old
                _prompt?.TrySetResult(null);
                _prompt = answer;
                PromptText = text;
                PromptKind = kind;
            });

            if (!queued) answer.TrySetResult(null);
            return answer.Task;
        }

        // --- Starting up ---

        /// <summary>
        /// Finds the Bluetooth radio and starts listing paired devices. Call once, on the UI thread.
        /// </summary>
        public async Task InitializeAsync()
        {
            Radio? radio = null;
            bool canControl = false;

            try
            {
                canControl = await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed;
                var radios = await Radio.GetRadiosAsync();
                radio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't find the Bluetooth radio: {ex.Message}");
            }

            // The page may have been closed while we were asking Windows
            if (_isDisposed) return;

            _radio = radio;
            _canControlRadio = canControl;
            _hasChecked = true;

            if (_radio != null)
            {
                _radio.StateChanged += Radio_StateChanged;
                IsRadioOn = _radio.State == RadioState.On;

                if (!_canControlRadio)
                {
                    Message = "Windows isn't letting SoundBar turn Bluetooth on or off. You can allow it in Windows privacy settings, under Radios.";
                }

                StartPairedWatchers();
            }

            OnPropertyChanged(nameof(AvailableVisibility));
            OnPropertyChanged(nameof(UnavailableVisibility));
            OnPropertyChanged(nameof(LoadingVisibility));
            OnPropertyChanged(nameof(CanToggleRadio));
        }

        // --- Bluetooth on/off ---

        private void Radio_StateChanged(Radio sender, object args)
        {
            RunOnUIThread(() =>
            {
                if (_isDisposed || _radio == null) return;

                IsRadioOn = _radio.State == RadioState.On;
                if (!IsRadioOn) StopScan();
            });
        }

        /// <summary>
        /// Turns Bluetooth on or off, the same as the switch in Windows Settings.
        /// </summary>
        public async Task SetRadioAsync(bool turnOn)
        {
            var radio = _radio;
            if (radio == null || _isRadioBusy || turnOn == IsRadioOn) return;

            ClearMessage();
            _isRadioBusy = true;
            OnPropertyChanged(nameof(CanToggleRadio));

            try
            {
                var result = await radio.SetStateAsync(turnOn ? RadioState.On : RadioState.Off);
                if (result != RadioAccessStatus.Allowed && !_isDisposed)
                {
                    Message = $"Windows didn't let SoundBar turn Bluetooth {(turnOn ? "on" : "off")}. You can allow it in Windows privacy settings, under Radios.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't change the Bluetooth radio: {ex.Message}");
                if (!_isDisposed) Message = $"Couldn't turn Bluetooth {(turnOn ? "on" : "off")}.";
            }
            finally
            {
                if (!_isDisposed)
                {
                    _isRadioBusy = false;
                    IsRadioOn = radio.State == RadioState.On;

                    // Always tell the switch, so it flips back if the change didn't happen
                    OnPropertyChanged(nameof(IsRadioOn));
                    OnPropertyChanged(nameof(CanToggleRadio));
                }
            }
        }

        // --- Paired devices ---

        private void StartPairedWatchers()
        {
            var selectors = new[]
            {
                BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)
            };

            foreach (var selector in selectors)
            {
                try
                {
                    var watcher = DeviceInformation.CreateWatcher(selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
                    watcher.Added += PairedWatcher_Added;
                    watcher.Updated += PairedWatcher_Updated;
                    watcher.Removed += PairedWatcher_Removed;
                    watcher.EnumerationCompleted += PairedWatcher_EnumerationCompleted;
                    _pairedWatchers.Add(watcher);
                    watcher.Start();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Couldn't watch paired Bluetooth devices: {ex.Message}");
                }
            }
        }

        private void PairedWatcher_Added(DeviceWatcher sender, DeviceInformation info)
        {
            RunOnUIThread(() =>
            {
                if (!IsPairedListLive() || !info.Pairing.IsPaired) return;

                _pairedEntries[info.Id] = info;
                RefreshPairedItem(GetGroupKey(info));
            });
        }

        private void PairedWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            RunOnUIThread(() =>
            {
                if (!IsPairedListLive() || !_pairedEntries.TryGetValue(update.Id, out var info)) return;

                string oldKey = GetGroupKey(info);
                info.Update(update);
                if (!info.Pairing.IsPaired) _pairedEntries.Remove(update.Id);

                RefreshPairedItem(oldKey);
                string newKey = GetGroupKey(info);
                if (newKey != oldKey) RefreshPairedItem(newKey);
            });
        }

        private void PairedWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            RunOnUIThread(() =>
            {
                if (!IsPairedListLive() || !_pairedEntries.Remove(update.Id, out var info)) return;
                RefreshPairedItem(GetGroupKey(info));
            });
        }

        private void PairedWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
        {
            RunOnUIThread(() =>
            {
                if (!IsPairedListLive()) return;
                _pairedListsLoaded++;
                OnPropertyChanged(nameof(NoPairedDevicesVisibility));
            });
        }

        /// <summary>
        /// Brings one device's row up to date from Windows' entries for it, adding or removing the row as needed.
        /// </summary>
        private void RefreshPairedItem(string key)
        {
            var entries = _pairedEntries.Values.Where(e => GetGroupKey(e) == key).ToList();
            var item = PairedDevices.FirstOrDefault(i => i.Key == key);

            if (entries.Count == 0)
            {
                if (item != null) PairedDevices.Remove(item);
                OnPropertyChanged(nameof(NoPairedDevicesVisibility));
                return;
            }

            bool isNew = item == null;
            item ??= new BluetoothDeviceItem(key, GetContainerId(entries[0])) { IsRadioOn = IsRadioOn };

            bool wasConnected = item.IsConnected;
            item.Entries.Clear();
            item.Entries.AddRange(entries);
            item.Name = GetName(entries);
            item.Kind = GetKind(entries);
            item.IsConnected = entries.Any(IsEntryConnected);

            PlaceInOrder(PairedDevices, item);
            if (isNew) OnPropertyChanged(nameof(NoPairedDevicesVisibility));

            // Speakers and headphones get their sound outputs once they've connected for the first time,
            // so look again whenever the connection changes
            if (isNew || wasConnected != item.IsConnected) CheckIfAudio(item);
        }

        private async void CheckIfAudio(BluetoothDeviceItem item)
        {
            var containerId = item.ContainerId;
            if (containerId == Guid.Empty) return;

            try
            {
                bool isAudio = await Task.Run(() => BluetoothAudioControl.IsAudioDevice(containerId));
                if (!_isDisposed) item.IsAudio = isAudio;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't check for Bluetooth audio: {ex.Message}");
            }
        }

        /// <summary>
        /// Keeps connected devices at the top, then sorts by name.
        /// </summary>
        private static void PlaceInOrder(ObservableCollection<BluetoothDeviceItem> list, BluetoothDeviceItem item)
        {
            int target = 0;
            foreach (var other in list)
            {
                if (!ReferenceEquals(other, item) && ComparePaired(other, item) < 0) target++;
            }

            int current = list.IndexOf(item);
            if (current < 0) list.Insert(target, item);
            else if (current != target) list.Move(current, target);
        }

        internal static int ComparePaired(BluetoothDeviceItem a, BluetoothDeviceItem b)
        {
            if (a.IsConnected != b.IsConnected) return a.IsConnected ? -1 : 1;
            int byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a.Key, b.Key);
        }

        // --- Connecting ---

        /// <summary>
        /// Connects a paired speaker or headset (or disconnects it if it's connected). Once it's connected,
        /// SoundBar switches the sound over to it, since that's why you'd connect it from a volume mixer.
        /// </summary>
        public async Task ToggleConnectionAsync(BluetoothDeviceItem item)
        {
            if (!item.CanAct || !item.IsAudio) return;

            ClearMessage();
            bool connect = !item.IsConnected;
            var containerId = item.ContainerId;
            item.BusyText = connect ? "Connecting…" : "Disconnecting…";

            try
            {
                bool accepted = await Task.Run(() => connect
                    ? BluetoothAudioControl.RequestConnect(containerId)
                    : BluetoothAudioControl.RequestDisconnect(containerId));
                if (_isDisposed) return;

                if (!accepted)
                {
                    Message = $"Windows wouldn't {(connect ? "connect" : "disconnect")} {item.Name}. Try More Bluetooth settings below.";
                    return;
                }

                // Windows tells us when the connection actually changes (see PairedWatcher_Updated)
                var giveUpAt = DateTime.UtcNow + ConnectTimeout;
                while (!_isDisposed && item.IsConnected != connect && DateTime.UtcNow < giveUpAt)
                {
                    await Task.Delay(250);
                }
                if (_isDisposed) return;

                if (item.IsConnected != connect)
                {
                    Message = connect
                        ? $"{item.Name} didn't connect. Make sure it's switched on and nearby, then try again."
                        : $"{item.Name} is taking a while to disconnect.";
                    return;
                }

                if (connect) await SwitchSoundToAsync(containerId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bluetooth connect failed: {ex.Message}");
                if (!_isDisposed) Message = $"Couldn't {(connect ? "connect" : "disconnect")} {item.Name}.";
            }
            finally
            {
                if (!_isDisposed) item.BusyText = null;
            }
        }

        /// <summary>
        /// Makes a just-connected speaker or headset the default sound output once Windows has it ready.
        /// </summary>
        private async Task SwitchSoundToAsync(Guid containerId)
        {
            var giveUpAt = DateTime.UtcNow + OutputReadyTimeout;
            while (!_isDisposed && DateTime.UtcNow < giveUpAt)
            {
                string? outputId = await Task.Run(() => BluetoothAudioControl.FindActiveOutput(containerId));
                if (_isDisposed) return;

                if (outputId != null)
                {
                    await Task.Run(() => AudioDeviceSwitcher.SetDefaultDevice(outputId));
                    return;
                }

                await Task.Delay(300);
            }
        }

        // --- Removing ---

        /// <summary>
        /// Unpairs a device, the same as "Remove device" in Windows Settings.
        /// </summary>
        public async Task RemoveAsync(BluetoothDeviceItem item)
        {
            if (item.IsBusy) return;

            ClearMessage();
            item.BusyText = "Removing…";
            bool removed = true;

            try
            {
                foreach (var entry in item.Entries.ToList())
                {
                    var result = await entry.Pairing.UnpairAsync();
                    if (result.Status is not (DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired))
                    {
                        removed = false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bluetooth unpair failed: {ex.Message}");
                removed = false;
            }

            if (_isDisposed) return;

            // On success Windows takes it off the list for us (see PairedWatcher_Removed)
            item.BusyText = null;
            if (!removed) Message = $"Couldn't remove {item.Name}. Try More Bluetooth settings below.";
        }

        // --- Finding and pairing new devices ---

        public void ToggleScan()
        {
            if (IsScanning) StopScan();
            else StartScan();
        }

        private void StartScan()
        {
            if (_isDisposed || IsScanning || !IsRadioOn) return;

            ClearMessage();

            // Windows' own "find unpaired devices" queries, which also tell the radio to go looking
            var selectors = new[]
            {
                BluetoothDevice.GetDeviceSelectorFromPairingState(false),
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(false)
            };

            foreach (var selector in selectors)
            {
                try
                {
                    var watcher = DeviceInformation.CreateWatcher(selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
                    watcher.Added += ScanWatcher_Added;
                    watcher.Updated += ScanWatcher_Updated;
                    watcher.Removed += ScanWatcher_Removed;
                    _scanWatchers.Add(watcher);
                    watcher.Start();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Couldn't search for Bluetooth devices: {ex.Message}");
                }
            }

            IsScanning = _scanWatchers.Count > 0;
            if (!IsScanning) Message = "Couldn't search for devices. Try More Bluetooth settings below.";
        }

        public void StopScan()
        {
            foreach (var watcher in _scanWatchers)
            {
                watcher.Added -= ScanWatcher_Added;
                watcher.Updated -= ScanWatcher_Updated;
                watcher.Removed -= ScanWatcher_Removed;
                StopWatcher(watcher);
            }
            _scanWatchers.Clear();
            _nearbyEntries.Clear();

            // Keep anything that's part-way through pairing, so it can finish
            foreach (var item in NearbyDevices.Where(i => !i.IsBusy).ToList())
            {
                NearbyDevices.Remove(item);
            }

            IsScanning = false;
        }

        private void ScanWatcher_Added(DeviceWatcher sender, DeviceInformation info)
        {
            RunOnUIThread(() =>
            {
                if (!IsScanLive()) return;
                _nearbyEntries[info.Id] = info;
                RefreshNearbyItem(info);
            });
        }

        private void ScanWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            RunOnUIThread(() =>
            {
                if (!IsScanLive() || !_nearbyEntries.TryGetValue(update.Id, out var info)) return;
                info.Update(update);
                RefreshNearbyItem(info);
            });
        }

        private void ScanWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            RunOnUIThread(() =>
            {
                if (!IsScanLive() || !_nearbyEntries.Remove(update.Id)) return;

                var item = NearbyDevices.FirstOrDefault(i => i.Key == GetEntryKey(update.Id));
                if (item != null && !item.IsBusy) NearbyDevices.Remove(item);
            });
        }

        private void RefreshNearbyItem(DeviceInformation info)
        {
            string key = GetEntryKey(info.Id);
            var item = NearbyDevices.FirstOrDefault(i => i.Key == key);
            var containerId = GetContainerId(info);

            // Like Windows, only list devices that have a name, can be paired, and aren't already ours
            bool show = !string.IsNullOrWhiteSpace(info.Name)
                && !info.Pairing.IsPaired
                && info.Pairing.CanPair
                && !(containerId != Guid.Empty && PairedDevices.Any(p => p.ContainerId == containerId));

            if (!show)
            {
                if (item != null && !item.IsBusy) NearbyDevices.Remove(item);
                return;
            }

            if (item == null)
            {
                item = new BluetoothDeviceItem(key, containerId) { IsRadioOn = IsRadioOn };
                item.Entries.Add(info);
                NearbyDevices.Add(item);
            }

            item.Name = info.Name;
            item.Kind = GetKind(item.Entries);
        }

        /// <summary>
        /// Pairs with a nearby device, asking the user to check or type a PIN if the device wants one.
        /// </summary>
        public async Task PairAsync(BluetoothDeviceItem item)
        {
            if (!item.CanAct || item.Entries.Count == 0) return;

            ClearMessage();
            var info = item.Entries[0];
            string name = item.Name;
            item.BusyText = "Pairing…";

            var pairing = info.Pairing.Custom;
            // Kept in a variable so exactly the same handler is removed again afterwards
            Windows.Foundation.TypedEventHandler<DeviceInformationCustomPairing, DevicePairingRequestedEventArgs> handler =
                (sender, args) => OnPairingRequested(args, name);
            pairing.PairingRequested += handler;

            try
            {
                var result = await pairing.PairAsync(SupportedPairingKinds, DevicePairingProtectionLevel.Default);
                if (_isDisposed) return;

                if (result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired)
                {
                    // It'll turn up under "Your devices" on its own
                    _nearbyEntries.Remove(info.Id);
                    NearbyDevices.Remove(item);
                }
                else
                {
                    Message = DescribePairingFailure(result.Status, name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bluetooth pairing failed: {ex.Message}");
                if (!_isDisposed) Message = $"Couldn't pair with {name}.";
            }
            finally
            {
                pairing.PairingRequested -= handler;
                if (!_isDisposed)
                {
                    HidePrompt();
                    item.BusyText = null;
                }
            }
        }

        /// <summary>
        /// Windows asking how to go ahead with pairing. Arrives on a background thread.
        /// </summary>
        private async void OnPairingRequested(DevicePairingRequestedEventArgs args, string name)
        {
            Windows.Foundation.Deferral? deferral = null;
            try
            {
                switch (args.PairingKind)
                {
                    case DevicePairingKinds.ConfirmOnly:
                        args.Accept();
                        break;

                    case DevicePairingKinds.DisplayPin:
                        // The device wants the user to type this PIN on it (usually a keyboard)
                        string shownPin = args.Pin;
                        RunOnUIThread(() =>
                        {
                            if (!_isDisposed) ShowInfo($"Type {shownPin} on {name}, then press Enter.");
                        });
                        args.Accept();
                        break;

                    case DevicePairingKinds.ConfirmPinMatch:
                        deferral = args.GetDeferral();
                        if (await AskAsync(BluetoothPromptKind.Confirm, $"Does {name} show the PIN {args.Pin}?") != null)
                        {
                            args.Accept();
                        }
                        break;

                    case DevicePairingKinds.ProvidePin:
                        deferral = args.GetDeferral();
                        string? typedPin = await AskAsync(BluetoothPromptKind.EnterPin, $"Type the PIN for {name}. It's often 0000 or 1234, or shown in the device's manual.");
                        if (typedPin != null) args.Accept(typedPin);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bluetooth pairing prompt failed: {ex.Message}");
            }
            finally
            {
                // Not accepting means no, which ends the pairing attempt
                deferral?.Complete();
            }
        }

        internal static string DescribePairingFailure(DevicePairingResultStatus status, string name) => status switch
        {
            DevicePairingResultStatus.PairingCanceled or DevicePairingResultStatus.RejectedByHandler => "Pairing cancelled.",
            DevicePairingResultStatus.AccessDenied => $"Windows didn't allow pairing with {name}.",
            _ => $"Couldn't pair with {name}. Make sure it's still in pairing mode and nearby, then try again."
        };

        // --- Reading Windows' device entries ---

        private static string GetEntryKey(string entryId) => "e:" + entryId;

        /// <summary>
        /// Entries for the same physical device share a container ID, so that's what we group by.
        /// </summary>
        private static string GetGroupKey(DeviceInformation info)
        {
            var containerId = GetContainerId(info);
            return containerId != Guid.Empty ? "c:" + containerId.ToString("D") : GetEntryKey(info.Id);
        }

        private static Guid GetContainerId(DeviceInformation info) =>
            info.Properties.TryGetValue(ContainerIdProperty, out var value) && value is Guid id ? id : Guid.Empty;

        private static bool IsEntryConnected(DeviceInformation info) =>
            info.Properties.TryGetValue(IsConnectedProperty, out var value) && value is bool connected && connected;

        private static string GetName(IEnumerable<DeviceInformation> entries) =>
            entries.Select(e => e.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Unknown device";

        private static BluetoothDeviceKind GetKind(IEnumerable<DeviceInformation> entries)
        {
            foreach (var entry in entries)
            {
                int? major = ReadNumber(entry, ClassMajorProperty);
                if (major is > 0)
                {
                    var kind = BluetoothDeviceItem.FromClassOfDevice(major.Value, ReadNumber(entry, ClassMinorProperty) ?? 0);
                    if (kind != BluetoothDeviceKind.Other) return kind;
                }

                int? appearance = ReadNumber(entry, LowEnergyAppearanceProperty);
                if (appearance is > 0)
                {
                    var kind = BluetoothDeviceItem.FromLowEnergyAppearance(appearance.Value);
                    if (kind != BluetoothDeviceKind.Other) return kind;
                }
            }

            return BluetoothDeviceKind.Other;
        }

        private static int? ReadNumber(DeviceInformation info, string property)
        {
            if (!info.Properties.TryGetValue(property, out var value) || value == null) return null;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }

        // --- Plumbing ---

        // Windows' events can arrive a moment after we've stopped listening (they're queued for the UI thread),
        // so each one checks we still want it. We go by our own state rather than which watcher sent it,
        // because Windows doesn't promise to hand back the same watcher object we created.

        private bool IsPairedListLive() => !_isDisposed;

        private bool IsScanLive() => !_isDisposed && IsScanning;

        private static void StopWatcher(DeviceWatcher watcher)
        {
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                {
                    watcher.Stop();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't stop a Bluetooth watcher: {ex.Message}");
            }
        }

        private bool RunOnUIThread(DispatcherQueueHandler action)
        {
            if (_dispatcher == null)
            {
                action();
                return true;
            }
            return _dispatcher.TryEnqueue(action);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Stops every watcher and lets go of Windows' device objects. Call on the UI thread.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            StopScan();
            _isDisposed = true;

            foreach (var watcher in _pairedWatchers)
            {
                watcher.Added -= PairedWatcher_Added;
                watcher.Updated -= PairedWatcher_Updated;
                watcher.Removed -= PairedWatcher_Removed;
                watcher.EnumerationCompleted -= PairedWatcher_EnumerationCompleted;
                StopWatcher(watcher);
            }
            _pairedWatchers.Clear();

            if (_radio != null)
            {
                _radio.StateChanged -= Radio_StateChanged;
                _radio = null;
            }

            // Anything still waiting on the user gets a "no", which ends that pairing attempt
            _prompt?.TrySetResult(null);
            _prompt = null;

            _pairedEntries.Clear();
            NearbyDevices.Clear();
            PairedDevices.Clear();
        }
    }
}
