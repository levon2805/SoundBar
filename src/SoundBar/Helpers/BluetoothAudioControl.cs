using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SoundBar.Helpers
{
    /// <summary>
    /// One of a Bluetooth device's sound outputs or microphones, as Windows sees it.
    /// </summary>
    internal readonly record struct BluetoothAudioEndpoint(string Id, bool IsOutput, bool IsActive, string? FilterId);

    /// <summary>
    /// Connects and disconnects Bluetooth speakers and headphones that are already paired.
    ///
    /// Windows has no simple "connect this device" call, so we do what the Settings app's Connect button
    /// does: find the device's sound outputs, follow each one down to the Bluetooth audio driver, and ask the
    /// driver to reconnect (or disconnect). The request uses KSPROPSETID_BtAudio from ksmedia.h, which every
    /// Windows Bluetooth audio driver supports and which doesn't need admin.
    ///
    /// Devices are matched by their container ID, which Windows gives to every part of one physical device
    /// (its Bluetooth entry, its speakers and its microphone all share it).
    ///
    /// Everything here talks to COM and can take a moment, so call it from a background thread.
    /// </summary>
    internal static class BluetoothAudioControl
    {
        /// <summary>
        /// Every sound output and microphone that belongs to this device, connected or not.
        /// Empty if it isn't an audio device.
        /// </summary>
        public static List<BluetoothAudioEndpoint> GetEndpoints(Guid containerId)
        {
            var results = new List<BluetoothAudioEndpoint>();
            if (containerId == Guid.Empty) return results;

            IMMDeviceEnumerator? enumerator = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                AddEndpoints(enumerator, EDataFlowRender, containerId, results);
                AddEndpoints(enumerator, EDataFlowCapture, containerId, results);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't list Bluetooth audio endpoints: {ex.Message}");
            }
            finally
            {
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }

            return results;
        }

        /// <summary>
        /// True if the device has speakers or a microphone that Windows knows about,
        /// so it's worth showing a Connect button for it.
        /// </summary>
        public static bool IsAudioDevice(Guid containerId) => GetEndpoints(containerId).Count > 0;

        /// <summary>
        /// The ID of the device's sound output once it's connected and ready to play, or null if it isn't yet.
        /// </summary>
        public static string? FindActiveOutput(Guid containerId) =>
            GetEndpoints(containerId).FirstOrDefault(e => e.IsOutput && e.IsActive).Id;

        /// <summary>
        /// Asks the device to connect. Returns true if the request was accepted. The connection itself
        /// takes a few seconds and can still fail (say, the device is switched off), so watch for it.
        /// </summary>
        public static bool RequestConnect(Guid containerId) => SendToDrivers(containerId, KSPROPERTY_ONESHOT_RECONNECT);

        /// <summary>
        /// Asks the device to disconnect. It stays paired, so it can be connected again later.
        /// </summary>
        public static bool RequestDisconnect(Guid containerId) => SendToDrivers(containerId, KSPROPERTY_ONESHOT_DISCONNECT);

        private static bool SendToDrivers(Guid containerId, uint propertyId)
        {
            // Headsets usually have two drivers (one for music, one for calls), so ask each of them once
            var filterIds = GetEndpoints(containerId)
                .Select(e => e.FilterId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (filterIds.Count == 0) return false;

            bool anyAccepted = false;
            IMMDeviceEnumerator? enumerator = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                foreach (var filterId in filterIds)
                {
                    if (SendToDriver(enumerator, filterId!, propertyId)) anyAccepted = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't reach the Bluetooth audio driver: {ex.Message}");
            }
            finally
            {
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }

            return anyAccepted;
        }

        private static bool SendToDriver(IMMDeviceEnumerator enumerator, string filterId, uint propertyId)
        {
            IMMDevice? filter = null;
            object? control = null;
            try
            {
                if (enumerator.GetDevice(filterId, out filter) != 0 || filter == null) return false;

                var iid = IID_IKsControl;
                if (filter.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out control) != 0 || control is not IKsControl ks) return false;

                // Drivers that aren't Bluetooth audio don't know this request and just say no, which is harmless
                var property = new KsProperty { Set = KSPROPSETID_BtAudio, Id = propertyId, Flags = KSPROPERTY_TYPE_GET };
                int status = ks.KsProperty(ref property, (uint)Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _);
                return status >= 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bluetooth audio request failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (control != null) Marshal.ReleaseComObject(control);
                if (filter != null) Marshal.ReleaseComObject(filter);
            }
        }

        private static void AddEndpoints(IMMDeviceEnumerator enumerator, int dataFlow, Guid containerId, List<BluetoothAudioEndpoint> results)
        {
            // Active, or unplugged (which is how a paired Bluetooth device looks while it's disconnected)
            if (enumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_ACTIVE | DEVICE_STATE_UNPLUGGED, out var collection) != 0 || collection == null) return;

            try
            {
                if (collection.GetCount(out uint count) != 0) return;

                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var device) != 0 || device == null) continue;

                    try
                    {
                        if (GetContainerId(device) != containerId) continue;
                        if (device.GetId(out var id) != 0 || string.IsNullOrEmpty(id)) continue;
                        if (device.GetState(out uint state) != 0) continue;

                        results.Add(new BluetoothAudioEndpoint(id, dataFlow == EDataFlowRender, state == DEVICE_STATE_ACTIVE, GetDriverId(device)));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Skipped an audio endpoint: {ex.Message}");
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }

        private static Guid GetContainerId(IMMDevice device)
        {
            if (device.OpenPropertyStore(STGM_READ, out var store) != 0 || store == null) return Guid.Empty;

            var value = new PropVariant();
            try
            {
                var key = PKEY_Device_ContainerId;
                if (store.GetValue(ref key, out value) != 0) return Guid.Empty;
                return value.vt == VT_CLSID && value.pointerValue != IntPtr.Zero
                    ? Marshal.PtrToStructure<Guid>(value.pointerValue)
                    : Guid.Empty;
            }
            finally
            {
                PropVariantClear(ref value);
                Marshal.ReleaseComObject(store);
            }
        }

        /// <summary>
        /// Follows a sound output down to the driver that runs it (Windows calls this the KS filter).
        /// </summary>
        private static string? GetDriverId(IMMDevice device)
        {
            object? topologyObject = null;
            IConnector? connector = null;
            try
            {
                var iid = IID_IDeviceTopology;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out topologyObject) != 0 || topologyObject is not IDeviceTopology topology) return null;
                if (topology.GetConnector(0, out connector) != 0 || connector == null) return null;
                return connector.GetDeviceIdConnectedTo(out var filterId) == 0 ? filterId : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (connector != null) Marshal.ReleaseComObject(connector);
                if (topologyObject != null) Marshal.ReleaseComObject(topologyObject);
            }
        }

        // --- Core Audio and Kernel Streaming COM interop (mmdeviceapi.h, devicetopology.h, ks.h, ksmedia.h) ---
        // Methods must stay in the same order as the headers, but we can stop after the last one we use.

        private const int EDataFlowRender = 0;
        private const int EDataFlowCapture = 1;
        private const uint DEVICE_STATE_ACTIVE = 0x1;
        private const uint DEVICE_STATE_UNPLUGGED = 0x8;
        private const uint CLSCTX_ALL = 0x17;
        private const uint STGM_READ = 0;
        private const ushort VT_CLSID = 72;

        private const uint KSPROPERTY_TYPE_GET = 0x1;
        private const uint KSPROPERTY_ONESHOT_RECONNECT = 0;
        private const uint KSPROPERTY_ONESHOT_DISCONNECT = 1;
        private static readonly Guid KSPROPSETID_BtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");

        private static readonly Guid IID_IDeviceTopology = new("2A07407E-6497-4A18-9787-32F79BD0D98F");
        private static readonly Guid IID_IKsControl = new("28F54685-06FD-11D2-B27A-00A0C9223196");
        private static readonly PropertyKey PKEY_Device_ContainerId = new() { fmtid = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), pid = 2 };

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
        }

        /// <summary>
        /// Just enough of PROPVARIANT to read a pointer-sized value (24 bytes on 64-bit, like the real thing).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort vt;
            public ushort reserved1;
            public ushort reserved2;
            public ushort reserved3;
            public IntPtr pointerValue;
            public IntPtr extra;
        }

        /// <summary>
        /// KSPROPERTY: which property set, which property in it, and whether we're getting or setting.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct KsProperty
        {
            public Guid Set;
            public uint Id;
            public uint Flags;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
            [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out uint state);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        }

        [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDeviceTopology
        {
            [PreserveSig] int GetConnectorCount(out uint count);
            [PreserveSig] int GetConnector(uint index, out IConnector connector);
        }

        [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IConnector
        {
            [PreserveSig] int GetType(out int type);
            [PreserveSig] int GetDataFlow(out int flow);
            [PreserveSig] int ConnectTo(IConnector connectTo);
            [PreserveSig] int Disconnect();
            [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
            [PreserveSig] int GetConnectedTo(out IConnector connectedTo);
            [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }

        [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IKsControl
        {
            [PreserveSig] int KsProperty(ref KsProperty property, uint propertyLength, IntPtr propertyData, uint dataLength, out uint bytesReturned);
        }
    }
}
