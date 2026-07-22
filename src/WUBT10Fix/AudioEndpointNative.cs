using System.Runtime.InteropServices;

namespace WUBT10Fix;

internal static class AudioEndpointNative
{
    private const uint DeviceStateActive = 0x00000001;
    private const uint ClassContextAll = 23;
    private const uint KsPropertyTypeGet = 0x00000001;
    private static readonly Guid EndpointVolumeInterface = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid DeviceTopologyInterface = new("2A07407E-6497-4A18-9787-32F79BD0D98F");
    private static readonly Guid KsControlInterface = new("28F54685-06FD-11D2-B27A-00A0C9223196");
    private static readonly Guid BluetoothAudioPropertySet = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");

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

    internal static BluetoothAudioControlResult RequestBluetoothAudioReconnect(string pnpInstanceId) =>
        RequestBluetoothAudioControl(pnpInstanceId, propertyId: 0, "reconnect");

    internal static BluetoothAudioControlResult RequestBluetoothAudioDisconnect(string pnpInstanceId) =>
        RequestBluetoothAudioControl(pnpInstanceId, propertyId: 1, "disconnect");

    private static BluetoothAudioControlResult RequestBluetoothAudioControl(
        string pnpInstanceId,
        uint propertyId,
        string operation)
    {
        const string prefix = "SWD\\MMDEVAPI\\";
        if (!pnpInstanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return BluetoothAudioControlResult.Failed("The target is not a Windows audio endpoint.");

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? endpoint = null;
        IDeviceTopology? topology = null;
        IntPtr topologyPointer = IntPtr.Zero;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
            if (enumerator.GetDevice(pnpInstanceId[prefix.Length..], out endpoint) != 0 || endpoint is null)
                return BluetoothAudioControlResult.Failed("Windows could not open the preserved WU-BT10 endpoint.");

            var topologyId = DeviceTopologyInterface;
            var activateTopology = endpoint.Activate(
                ref topologyId,
                ClassContextAll,
                IntPtr.Zero,
                out topologyPointer);
            if (activateTopology != 0 || topologyPointer == IntPtr.Zero)
                return BluetoothAudioControlResult.Failed(
                    $"Windows could not open the WU-BT10 audio topology (0x{activateTopology:X8}).",
                    activateTopology);

            topology = (IDeviceTopology)Marshal.GetObjectForIUnknown(topologyPointer);
            var countResult = topology.GetConnectorCount(out var connectorCount);
            if (countResult != 0)
                return BluetoothAudioControlResult.Failed(
                    $"Windows could not enumerate the WU-BT10 audio topology (0x{countResult:X8}).",
                    countResult);

            for (uint index = 0; index < connectorCount; index++)
            {
                IConnector? connector = null;
                IntPtr connectedDeviceIdPointer = IntPtr.Zero;
                try
                {
                    if (topology.GetConnector(index, out connector) != 0 || connector is null)
                        continue;

                    var connectedIdResult = connector.GetDeviceIdConnectedTo(out connectedDeviceIdPointer);
                    if (connectedIdResult != 0 || connectedDeviceIdPointer == IntPtr.Zero)
                        continue;

                    var connectedDeviceId = Marshal.PtrToStringUni(connectedDeviceIdPointer);
                    if (string.IsNullOrWhiteSpace(connectedDeviceId)
                        || !connectedDeviceId.StartsWith("{2}.\\\\?\\bth", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var result = SendBluetoothAudioProperty(
                        enumerator,
                        connectedDeviceId,
                        propertyId,
                        operation);
                    if (result.Success)
                        return result;
                }
                finally
                {
                    if (connectedDeviceIdPointer != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(connectedDeviceIdPointer);
                    if (connector is not null)
                        Marshal.FinalReleaseComObject(connector);
                }
            }

            return BluetoothAudioControlResult.Failed(
                "The WU-BT10 endpoint did not expose a Bluetooth audio kernel-streaming filter.");
        }
        catch (COMException ex)
        {
            return BluetoothAudioControlResult.Failed(
                $"Windows rejected the Bluetooth audio {operation} request (0x{ex.HResult:X8}).",
                ex.HResult);
        }
        finally
        {
            if (topologyPointer != IntPtr.Zero)
                Marshal.Release(topologyPointer);
            if (topology is not null)
                Marshal.FinalReleaseComObject(topology);
            if (endpoint is not null)
                Marshal.FinalReleaseComObject(endpoint);
            if (enumerator is not null)
                Marshal.FinalReleaseComObject(enumerator);
        }
    }

    private static BluetoothAudioControlResult SendBluetoothAudioProperty(
        IMMDeviceEnumerator enumerator,
        string connectedDeviceId,
        uint propertyId,
        string operation)
    {
        IMMDevice? filterDevice = null;
        IKsControl? ksControl = null;
        IntPtr ksControlPointer = IntPtr.Zero;
        try
        {
            var getDeviceResult = enumerator.GetDevice(connectedDeviceId, out filterDevice);
            if (getDeviceResult != 0 || filterDevice is null)
                return BluetoothAudioControlResult.Failed(
                    $"Windows could not open the Bluetooth audio filter (0x{getDeviceResult:X8}).",
                    getDeviceResult,
                    connectedDeviceId);

            var controlId = KsControlInterface;
            var activateResult = filterDevice.Activate(
                ref controlId,
                ClassContextAll,
                IntPtr.Zero,
                out ksControlPointer);
            if (activateResult != 0 || ksControlPointer == IntPtr.Zero)
                return BluetoothAudioControlResult.Failed(
                    $"Windows could not open Bluetooth audio control (0x{activateResult:X8}).",
                    activateResult,
                    connectedDeviceId);

            ksControl = (IKsControl)Marshal.GetObjectForIUnknown(ksControlPointer);
            var property = new KsProperty(BluetoothAudioPropertySet, propertyId, KsPropertyTypeGet);
            var status = ksControl.KsProperty(
                ref property,
                checked((uint)Marshal.SizeOf<KsProperty>()),
                IntPtr.Zero,
                0,
                out _);
            return status == 0
                ? BluetoothAudioControlResult.Completed(
                    $"Windows accepted the Bluetooth audio {operation} request.",
                    connectedDeviceId)
                : BluetoothAudioControlResult.Failed(
                    $"The Bluetooth audio driver rejected the {operation} request (0x{status:X8}).",
                    status,
                    connectedDeviceId);
        }
        finally
        {
            if (ksControlPointer != IntPtr.Zero)
                Marshal.Release(ksControlPointer);
            if (ksControl is not null)
                Marshal.FinalReleaseComObject(ksControl);
            if (filterDevice is not null)
                Marshal.FinalReleaseComObject(filterDevice);
        }
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
    [Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        [PreserveSig]
        int GetConnectorCount(out uint count);

        [PreserveSig]
        int GetConnector(uint index, out IConnector connector);
    }

    [ComImport]
    [Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig] int GetType(out int connectorType);
        [PreserveSig] int GetDataFlow(out int dataFlow);
        [PreserveSig] int ConnectTo(IConnector connector);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector connector);
        [PreserveSig] int GetConnectorIdConnectedTo(out IntPtr connectorId);
        [PreserveSig] int GetDeviceIdConnectedTo(out IntPtr deviceId);
    }

    [ComImport]
    [Guid("28F54685-06FD-11D2-B27A-00A0C9223196")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig]
        int KsProperty(
            ref KsProperty property,
            uint propertyLength,
            IntPtr propertyData,
            uint dataLength,
            out uint bytesReturned);

        [PreserveSig]
        int KsMethod(
            ref KsProperty method,
            uint methodLength,
            IntPtr methodData,
            uint dataLength,
            out uint bytesReturned);

        [PreserveSig]
        int KsEvent(
            ref KsProperty ksEvent,
            uint eventLength,
            IntPtr eventData,
            uint dataLength,
            out uint bytesReturned);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KsProperty(Guid set, uint id, uint flags)
    {
        internal readonly Guid Set = set;
        internal readonly uint Id = id;
        internal readonly uint Flags = flags;
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

internal sealed record BluetoothAudioControlResult(
    bool Success,
    string Message,
    int Status = 0,
    string? FilterDeviceId = null)
{
    internal static BluetoothAudioControlResult Completed(string message, string filterDeviceId) =>
        new(true, message, 0, filterDeviceId);

    internal static BluetoothAudioControlResult Failed(
        string message,
        int status = -1,
        string? filterDeviceId = null) =>
        new(false, message, status, filterDeviceId);
}
