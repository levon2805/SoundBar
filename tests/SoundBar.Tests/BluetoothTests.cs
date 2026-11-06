using System;
using SoundBar.Models;
using SoundBar.ViewModels;
using Windows.Devices.Enumeration;
using Xunit;

namespace SoundBar.Tests
{
    /// <summary>
    /// v4.3.0: the Bluetooth page's device types, ordering and messages.
    /// (Talking to real Bluetooth hardware isn't something a unit test can do, so that's checked by hand.)
    /// </summary>
    public class BluetoothTests
    {
        [Theory]
        [InlineData(4, 1, BluetoothDeviceKind.Headset)]     // Wearable headset
        [InlineData(4, 2, BluetoothDeviceKind.Headset)]     // Hands-free
        [InlineData(4, 4, BluetoothDeviceKind.Microphone)]
        [InlineData(4, 5, BluetoothDeviceKind.Speaker)]     // Loudspeaker (what a Logi Z207 reports)
        [InlineData(4, 6, BluetoothDeviceKind.Headphones)]
        [InlineData(4, 7, BluetoothDeviceKind.Speaker)]     // Portable audio
        [InlineData(4, 8, BluetoothDeviceKind.Car)]
        [InlineData(4, 0, BluetoothDeviceKind.Speaker)]     // Audio, but it didn't say which kind
        [InlineData(5, 0x10, BluetoothDeviceKind.Keyboard)]
        [InlineData(5, 0x20, BluetoothDeviceKind.Mouse)]
        [InlineData(5, 0x30, BluetoothDeviceKind.Keyboard)] // Keyboard and pointer combo
        [InlineData(5, 0x02, BluetoothDeviceKind.Gamepad)]
        [InlineData(5, 0x01, BluetoothDeviceKind.Gamepad)]  // Joystick
        [InlineData(2, 3, BluetoothDeviceKind.Phone)]
        [InlineData(1, 3, BluetoothDeviceKind.Computer)]
        [InlineData(6, 0, BluetoothDeviceKind.Other)]       // Imaging (printers, cameras)
        public void ClassOfDevice_PicksTheRightKind(int major, int minor, BluetoothDeviceKind expected)
        {
            Assert.Equal(expected, BluetoothDeviceItem.FromClassOfDevice(major, minor));
        }

        [Theory]
        [InlineData(0x03C1, BluetoothDeviceKind.Keyboard)]
        [InlineData(0x03C2, BluetoothDeviceKind.Mouse)]
        [InlineData(0x03C4, BluetoothDeviceKind.Gamepad)]
        [InlineData(0x0941, BluetoothDeviceKind.Headphones)] // Earbuds
        [InlineData(0x0942, BluetoothDeviceKind.Headset)]
        [InlineData(0x0943, BluetoothDeviceKind.Headphones)]
        [InlineData(0x0841, BluetoothDeviceKind.Speaker)]
        [InlineData(0x0040, BluetoothDeviceKind.Phone)]
        [InlineData(0x00C1, BluetoothDeviceKind.Other)]      // Watch
        public void LowEnergyAppearance_PicksTheRightKind(int appearance, BluetoothDeviceKind expected)
        {
            Assert.Equal(expected, BluetoothDeviceItem.FromLowEnergyAppearance(appearance));
        }

        [Fact]
        public void EveryKind_HasAnIconAndAName()
        {
            foreach (BluetoothDeviceKind kind in Enum.GetValues(typeof(BluetoothDeviceKind)))
            {
                Assert.Single(BluetoothDeviceItem.GetGlyph(kind));
                Assert.False(string.IsNullOrWhiteSpace(BluetoothDeviceItem.GetKindText(kind)));
            }
        }

        [Fact]
        public void PairedDevices_ConnectedComeFirst_ThenByName()
        {
            var connectedZ = new BluetoothDeviceItem("a", Guid.NewGuid()) { Name = "Zebra Speaker", IsConnected = true };
            var apple = new BluetoothDeviceItem("b", Guid.NewGuid()) { Name = "apple keyboard" };
            var banana = new BluetoothDeviceItem("c", Guid.NewGuid()) { Name = "Banana Buds" };

            Assert.True(BluetoothViewModel.ComparePaired(connectedZ, apple) < 0);
            Assert.True(BluetoothViewModel.ComparePaired(apple, banana) < 0);
            Assert.True(BluetoothViewModel.ComparePaired(banana, connectedZ) > 0);
        }

        [Fact]
        public void DeviceButtons_FollowItsState()
        {
            var item = new BluetoothDeviceItem("a", Guid.NewGuid()) { Name = "Logi Z207" };

            // Not audio: nothing to connect, so no button
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, item.ConnectButtonVisibility);

            item.IsAudio = true;
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, item.ConnectButtonVisibility);
            Assert.Equal("Connect", item.ConnectButtonText);
            Assert.Equal("Not connected", item.StatusText);
            Assert.True(item.CanAct);

            item.BusyText = "Connecting…";
            Assert.Equal("Connecting…", item.StatusText);
            Assert.False(item.CanAct);
            Assert.False(item.IsIdle);

            item.BusyText = null;
            item.IsConnected = true;
            Assert.Equal("Disconnect", item.ConnectButtonText);
            Assert.Equal("Connected", item.StatusText);

            // Nothing can connect while Bluetooth is off
            item.IsRadioOn = false;
            Assert.False(item.CanAct);
        }

        [Fact]
        public void PairingFailures_GetAPlainMessage()
        {
            Assert.Equal("Pairing cancelled.", BluetoothViewModel.DescribePairingFailure(DevicePairingResultStatus.PairingCanceled, "Buds"));
            Assert.Equal("Pairing cancelled.", BluetoothViewModel.DescribePairingFailure(DevicePairingResultStatus.RejectedByHandler, "Buds"));
            Assert.Contains("Buds", BluetoothViewModel.DescribePairingFailure(DevicePairingResultStatus.AuthenticationTimeout, "Buds"));
            Assert.Contains("pairing mode", BluetoothViewModel.DescribePairingFailure(DevicePairingResultStatus.Failed, "Buds"));
        }
    }
}
