using System.Runtime.InteropServices;

namespace WUBT10Fix;

internal static class AudioEndpointNative
{
    private const uint DeviceStateActive = 0x00000001;
    private const uint ClassContextAll = 23;
    private static readonly Guid EndpointVolumeInterface = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    internal static bool IsActive(string pnpInstanceId)
    {
        const string prefix = "SWD\\MMDEVAPI\\";
        if (!pnpInstanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var endpointId = pnpInstanceId[prefix.Length..];
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
            if (enumerator.GetDevice(endpointId, out device) != 0 || device is null)
                return false;

            return device.GetState(out var state) == 0 && state == DeviceStateActive;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (device is not null)
                Marshal.FinalReleaseComObject(device);
            if (enumerator is not null)
                Marshal.FinalReleaseComObject(enumerator);
        }
    }

    internal static bool TryGetVolume(string pnpInstanceId, out float volume)
    {
        var currentVolume = 0f;
        var success = WithEndpointVolume(
            pnpInstanceId,
            endpoint => endpoint.GetMasterVolumeLevelScalar(out currentVolume) == 0);
        volume = currentVolume;
        return success;
    }

    internal static bool SetVolume(string pnpInstanceId, float volume)
    {
        var safeValue = Math.Clamp(volume, 0f, 1f);
        return WithEndpointVolume(pnpInstanceId, endpoint =>
        {
            var eventContext = Guid.Empty;
            return endpoint.SetMasterVolumeLevelScalar(safeValue, ref eventContext) == 0;
        });
    }

    private static bool WithEndpointVolume(string pnpInstanceId, Func<IAudioEndpointVolume, bool> action)
    {
        const string prefix = "SWD\\MMDEVAPI\\";
        if (!pnpInstanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpointVolume = null;
        IntPtr interfacePointer = IntPtr.Zero;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
            if (enumerator.GetDevice(pnpInstanceId[prefix.Length..], out device) != 0 || device is null)
                return false;

            var interfaceId = EndpointVolumeInterface;
            if (device.Activate(ref interfaceId, ClassContextAll, IntPtr.Zero, out interfacePointer) != 0 || interfacePointer == IntPtr.Zero)
                return false;

            endpointVolume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(interfacePointer);
            return action(endpointVolume);
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (interfacePointer != IntPtr.Zero)
                Marshal.Release(interfacePointer);
            if (endpointVolume is not null)
                Marshal.FinalReleaseComObject(endpointVolume);
            if (device is not null)
                Marshal.FinalReleaseComObject(device);
            if (enumerator is not null)
                Marshal.FinalReleaseComObject(enumerator);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParameters, out IntPtr interfacePointer);

        [PreserveSig]
        int OpenPropertyStore(uint access, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    }
}
