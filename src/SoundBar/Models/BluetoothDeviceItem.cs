using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Windows.Devices.Enumeration;

namespace SoundBar.Models
{
    /// <summary>
    /// What sort of thing a Bluetooth device is, so we can show the right icon.
    /// </summary>
    public enum BluetoothDeviceKind
    {
        Other,
        Headphones,
        Headset,
        Speaker,
        Microphone,
        Car,
        Mouse,
        Keyboard,
        Gamepad,
        Phone,
        Computer
    }

    /// <summary>
    /// One Bluetooth device on the Bluetooth page: either one that's paired with this PC,
    /// or one nearby that's waiting to be paired.
    ///
    /// Some devices show up to Windows more than once (classic Bluetooth and Bluetooth LE), so one item can
    /// hold several Windows entries. They all share a container ID, which is how we know they're the same thing.
    /// </summary>
    public class BluetoothDeviceItem : INotifyPropertyChanged
    {
        public BluetoothDeviceItem(string key, Guid containerId)
        {
            Key = key;
            ContainerId = containerId;
        }

        /// <summary>
        /// What we group Windows' entries by: the container ID if there is one, otherwise the entry's own ID.
        /// </summary>
        public string Key { get; }

        public Guid ContainerId { get; }

        /// <summary>
        /// Windows' own entries for this device. Only touched on the UI thread.
        /// </summary>
        internal List<DeviceInformation> Entries { get; } = new();

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        private BluetoothDeviceKind _kind;
        public BluetoothDeviceKind Kind
        {
            get => _kind;
            set
            {
                if (SetField(ref _kind, value)) OnPropertyChanged(nameof(Glyph));
            }
        }

        /// <summary>
        /// Icon from the system icon font. Every one of these is in both the Windows 10 and Windows 11 fonts.
        /// </summary>
        public string Glyph => GetGlyph(Kind);

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetField(ref _isConnected, value)) RaiseStateChanged();
            }
        }

        private bool _isAudio;
        /// <summary>
        /// True for speakers, headphones and headsets, which are the devices we can connect and disconnect.
        /// Mice, keyboards and controllers connect themselves when they're switched on.
        /// </summary>
        public bool IsAudio
        {
            get => _isAudio;
            set
            {
                if (SetField(ref _isAudio, value)) RaiseStateChanged();
            }
        }

        private string? _busyText;
        /// <summary>
        /// Set while something is happening to this device ("Connecting…", "Pairing…"), null otherwise.
        /// </summary>
        public string? BusyText
        {
            get => _busyText;
            set
            {
                if (SetField(ref _busyText, value)) RaiseStateChanged();
            }
        }

        public bool IsBusy => BusyText != null;

        public bool IsIdle => !IsBusy;

        private bool _isRadioOn = true;
        /// <summary>
        /// Nothing can connect or pair while Bluetooth is off, so the buttons grey out.
        /// </summary>
        public bool IsRadioOn
        {
            get => _isRadioOn;
            set
            {
                if (SetField(ref _isRadioOn, value)) RaiseStateChanged();
            }
        }

        public bool CanAct => !IsBusy && IsRadioOn;

        /// <summary>
        /// The line under the name for a paired device.
        /// </summary>
        public string StatusText => BusyText ?? (IsConnected ? "Connected" : "Not connected");

        public string ConnectButtonText => IsConnected ? "Disconnect" : "Connect";

        public Visibility ConnectButtonVisibility => IsAudio ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// The line under the name for a nearby device.
        /// </summary>
        public string NearbyStatusText => BusyText ?? GetKindText(Kind);

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(CanAct));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(NearbyStatusText));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(ConnectButtonVisibility));
        }

        /// <summary>
        /// Works out the device type from its classic Bluetooth "class of device".
        /// Windows hands us the major class and the 6-bit minor class as separate numbers.
        /// </summary>
        internal static BluetoothDeviceKind FromClassOfDevice(int major, int minor)
        {
            switch (major)
            {
                case 1: return BluetoothDeviceKind.Computer;
                case 2: return BluetoothDeviceKind.Phone;
                case 4: // Audio/video
                    return minor switch
                    {
                        1 or 2 => BluetoothDeviceKind.Headset,  // Wearable headset, hands-free
                        4 => BluetoothDeviceKind.Microphone,
                        5 or 7 or 10 => BluetoothDeviceKind.Speaker, // Loudspeaker, portable audio, hi-fi
                        6 => BluetoothDeviceKind.Headphones,
                        8 => BluetoothDeviceKind.Car,
                        _ => BluetoothDeviceKind.Speaker
                    };
                case 5: // Peripheral: the top two bits say keyboard/pointer, the bottom four the device type
                    int type = minor & 0x0F;
                    int inputs = (minor >> 4) & 0x03;
                    if (type == 1 || type == 2) return BluetoothDeviceKind.Gamepad; // Joystick, gamepad
                    if (inputs == 2) return BluetoothDeviceKind.Mouse;
                    if (inputs == 1 || inputs == 3) return BluetoothDeviceKind.Keyboard;
                    return BluetoothDeviceKind.Other;
                default:
                    return BluetoothDeviceKind.Other;
            }
        }

        /// <summary>
        /// Works out the device type from a Bluetooth LE "appearance" value
        /// (category in the top ten bits, subcategory in the bottom six).
        /// </summary>
        internal static BluetoothDeviceKind FromLowEnergyAppearance(int appearance)
        {
            int category = appearance >> 6;
            int subcategory = appearance & 0x3F;

            return category switch
            {
                0x001 => BluetoothDeviceKind.Phone,
                0x002 => BluetoothDeviceKind.Computer,
                0x00F => subcategory switch // Human interface device
                {
                    1 => BluetoothDeviceKind.Keyboard,
                    2 => BluetoothDeviceKind.Mouse,
                    3 or 4 => BluetoothDeviceKind.Gamepad, // Joystick, gamepad
                    _ => BluetoothDeviceKind.Other
                },
                0x021 => BluetoothDeviceKind.Speaker, // Audio sink
                0x025 => subcategory == 2 ? BluetoothDeviceKind.Headset : BluetoothDeviceKind.Headphones, // Earbuds, headset, headphones
                _ => BluetoothDeviceKind.Other
            };
        }

        internal static string GetGlyph(BluetoothDeviceKind kind) => kind switch
        {
            BluetoothDeviceKind.Headphones => "",
            BluetoothDeviceKind.Headset => "",
            BluetoothDeviceKind.Speaker => "",
            BluetoothDeviceKind.Microphone => "",
            BluetoothDeviceKind.Car => "",
            BluetoothDeviceKind.Mouse => "",
            BluetoothDeviceKind.Keyboard => "",
            BluetoothDeviceKind.Gamepad => "",
            BluetoothDeviceKind.Phone => "",
            BluetoothDeviceKind.Computer => "",
            _ => "" // Bluetooth
        };

        internal static string GetKindText(BluetoothDeviceKind kind) => kind switch
        {
            BluetoothDeviceKind.Headphones => "Headphones",
            BluetoothDeviceKind.Headset => "Headset",
            BluetoothDeviceKind.Speaker => "Speaker",
            BluetoothDeviceKind.Microphone => "Microphone",
            BluetoothDeviceKind.Car => "Car audio",
            BluetoothDeviceKind.Mouse => "Mouse",
            BluetoothDeviceKind.Keyboard => "Keyboard",
            BluetoothDeviceKind.Gamepad => "Controller",
            BluetoothDeviceKind.Phone => "Phone",
            BluetoothDeviceKind.Computer => "Computer",
            _ => "Bluetooth device"
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
