using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SoundBar.Helpers
{
    /// <summary>
    /// The network types Windows lets you pick between (Settings > Network &amp; internet).
    /// The numbers match what Windows uses.
    /// </summary>
    public enum NetworkCategory
    {
        Public = 0,
        Private = 1,
        Domain = 2
    }

    /// <summary>
    /// What we need to know about one network the PC is on.
    /// </summary>
    public readonly record struct NetworkInfo(bool IsConnectedToInternet, NetworkCategory Category);

    /// <summary>
    /// Asks Windows what kind of network we're on, so we can warn before opening the companion server
    /// on something like café or hotel Wi-Fi.
    /// </summary>
    public static class NetworkHelper
    {
        /// <summary>
        /// True if Windows has the network we're using set to Public. If Windows can't tell us,
        /// this says false rather than nagging the user over nothing.
        /// </summary>
        public static bool IsOnPublicNetwork()
        {
            try
            {
                return ShouldTreatAsPublic(GetConnectedNetworks());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't check the network type: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Decides whether to warn. We only look at networks with internet access, because virtual
        /// adapters (WSL, Hyper-V, VPNs) often show up as "Unidentified network" on Public and would
        /// warn people for no reason. If nothing has internet (say an offline LAN), we look at everything.
        /// </summary>
        internal static bool ShouldTreatAsPublic(IReadOnlyCollection<NetworkInfo> networks)
        {
            var online = networks.Where(n => n.IsConnectedToInternet).ToList();
            var relevant = online.Count > 0 ? online : networks;
            return relevant.Any(n => n.Category == NetworkCategory.Public);
        }

        private static List<NetworkInfo> GetConnectedNetworks()
        {
            var results = new List<NetworkInfo>();
            var managerType = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), throwOnError: true)!;
            var manager = (INetworkListManager)Activator.CreateInstance(managerType)!;

            try
            {
                var networks = manager.GetNetworks(NLM_ENUM_NETWORK_CONNECTED);
                while (networks.Next(1, out var network, out uint fetched) == 0 && fetched == 1)
                {
                    try
                    {
                        results.Add(new NetworkInfo(network.IsConnectedToInternet, (NetworkCategory)network.GetCategory()));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(network);
                    }
                }
                Marshal.ReleaseComObject(networks);
            }
            finally
            {
                Marshal.ReleaseComObject(manager);
            }

            return results;
        }

        // --- Network List Manager COM interop (from netlistmgr.h) ---
        // Methods must stay in the same order as the header, but we can stop after the last one we use.

        private const int NLM_ENUM_NETWORK_CONNECTED = 0x1;

        [ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface INetworkListManager
        {
            IEnumNetworks GetNetworks(int flags);
        }

        [ComImport, Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IEnumNetworks
        {
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object GetNewEnum();

            [PreserveSig]
            int Next(uint count, out INetwork network, out uint fetched);
        }

        [ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface INetwork
        {
            [return: MarshalAs(UnmanagedType.BStr)] string GetName();
            void SetName([MarshalAs(UnmanagedType.BStr)] string name);
            [return: MarshalAs(UnmanagedType.BStr)] string GetDescription();
            void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);
            Guid GetNetworkId();
            int GetDomainType();
            [return: MarshalAs(UnmanagedType.IUnknown)] object GetNetworkConnections();
            void GetTimeCreatedAndConnected(out uint lowCreated, out uint highCreated, out uint lowConnected, out uint highConnected);
            bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
            bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
            int GetConnectivity();
            int GetCategory();
        }
    }
}
