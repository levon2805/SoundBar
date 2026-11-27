using System;
using System.Runtime.InteropServices;

namespace SoundBar.Services
{
    // Windows 10/11 (newer)
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, out IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, out IntPtr pmftDefaultPeriod, out IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr pMode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bFxStore, IntPtr key, out IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bVisible);
    }

    // Windows 11 (some builds) / Windows 10 (Vista compatible)
    [Guid("568b9108-44bf-40b4-9006-86afe5b5a620")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfigVista
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, out IntPtr ppFormat);
        // Note: No ResetDeviceFormat in this interface
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, out IntPtr pmftDefaultPeriod, out IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr pMode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bFxStore, IntPtr key, out IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bVisible);
    }

    // Classic Windows 7/8
    [Guid("f8679f50-850a-41ce-8224-cb8a37f5e3b6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfigClassic
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, out IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, out IntPtr pmftDefaultPeriod, out IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr pMode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bFxStore, IntPtr key, out IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bVisible);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    internal class PolicyConfigClient
    {
    }

    /// <summary>
    /// A sneaky little helper class that talks directly to undocumented Windows COM APIs 
    /// to switch the default audio output device. It tries a few different versions of the API 
    /// to make sure it works across Windows 7, 10, and 11.
    /// </summary>
    public static class AudioDeviceSwitcher
    {
        /// <summary>
        /// Tells Windows to instantly switch all its audio over to the specified device.
        /// </summary>
        /// <param name="deviceId">The unique ID of the device you want to switch to.</param>
        /// <returns>
        /// True if Windows accepted it. This uses an interface Windows doesn't officially document, which a future
        /// Windows could change, so callers say so rather than the device picker silently doing nothing.
        /// </returns>
        public static bool SetDefaultDevice(string deviceId)
        {
            object? pcc = null;
            try
            {
                pcc = new PolicyConfigClient();

                // Each role: console (0), multimedia (1), communications (2). It worked if multimedia did,
                // since that's the one SoundBar and most apps follow.
                static bool Apply(Func<int, int> setForRole)
                {
                    setForRole(0);
                    int multimedia = setForRole(1);
                    setForRole(2);
                    return multimedia >= 0;
                }

                // Try Windows 10/11 version
                if (pcc is IPolicyConfig config)
                    return Apply(role => config.SetDefaultEndpoint(deviceId, role));

                // Try alternate Windows 11/Vista version
                if (pcc is IPolicyConfigVista configVista)
                    return Apply(role => configVista.SetDefaultEndpoint(deviceId, role));

                // Try classic Windows 7/8 version
                if (pcc is IPolicyConfigClassic configClassic)
                    return Apply(role => configClassic.SetDefaultEndpoint(deviceId, role));

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Couldn't switch the default device: {ex.Message}");
                return false;
            }
            finally
            {
                if (pcc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(pcc);
            }
        }
    }
}
