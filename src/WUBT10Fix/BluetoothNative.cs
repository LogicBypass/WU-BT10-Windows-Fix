using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WUBT10Fix;

internal static class BluetoothNative
{
    internal static List<BluetoothDeviceInfo> FindWuBt10AudioDevices()
    {
        var matches = new List<BluetoothDeviceInfo>();
        foreach (var item in EnumerateDevices())
        {
            if (item.Name?.Contains("WU-BT10 AUDIO", StringComparison.OrdinalIgnoreCase) == true)
                matches.Add(item);
        }

        return matches;
    }

    internal static string FormatAddress(ulong address)
    {
        var bytes = BitConverter.GetBytes(address);
        return string.Join(":", bytes.Take(6).Reverse().Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static List<BluetoothDeviceInfo> EnumerateDevices()
    {
        var result = new List<BluetoothDeviceInfo>();
        var findParameters = new BluetoothFindRadioParameters { Size = Marshal.SizeOf<BluetoothFindRadioParameters>() };
        var radioFind = BluetoothFindFirstRadio(ref findParameters, out var firstRadio);
        if (radioFind == IntPtr.Zero)
            return result;

        try
        {
            var radio = firstRadio;
            while (true)
            {
                var search = new BluetoothDeviceSearchParameters
                {
                    Size = Marshal.SizeOf<BluetoothDeviceSearchParameters>(),
                    ReturnAuthenticated = true,
                    ReturnRemembered = true,
                    ReturnUnknown = true,
                    ReturnConnected = true,
                    IssueInquiry = false,
                    TimeoutMultiplier = 1,
                    RadioHandle = radio
                };

                var device = new BluetoothDeviceInfo { Size = Marshal.SizeOf<BluetoothDeviceInfo>() };
                var deviceFind = BluetoothFindFirstDevice(ref search, ref device);
                if (deviceFind != IntPtr.Zero)
                {
                    try
                    {
                        do
                        {
                            result.Add(device);
                            device.Size = Marshal.SizeOf<BluetoothDeviceInfo>();
                        }
                        while (BluetoothFindNextDevice(deviceFind, ref device));
                    }
                    finally
                    {
                        BluetoothFindDeviceClose(deviceFind);
                    }
                }

                if (!BluetoothFindNextRadio(radioFind, out var nextRadio))
                    break;

                radio.Dispose();
                radio = nextRadio;
            }

            radio.Dispose();
        }
        finally
        {
            BluetoothFindRadioClose(radioFind);
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BluetoothFindRadioParameters
    {
        internal int Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemTime
    {
        internal ushort Year;
        internal ushort Month;
        internal ushort DayOfWeek;
        internal ushort Day;
        internal ushort Hour;
        internal ushort Minute;
        internal ushort Second;
        internal ushort Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct BluetoothDeviceInfo
    {
        internal int Size;
        internal ulong Address;
        internal uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] internal bool Connected;
        [MarshalAs(UnmanagedType.Bool)] internal bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] internal bool Authenticated;
        internal SystemTime LastSeen;
        internal SystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] internal string? Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BluetoothDeviceSearchParameters
    {
        internal int Size;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] internal bool IssueInquiry;
        internal byte TimeoutMultiplier;
        internal SafeFileHandle RadioHandle;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BluetoothFindRadioParameters parameters, out SafeFileHandle radio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextRadio(IntPtr find, out SafeFileHandle radio);

    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr find);

    [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BluetoothDeviceSearchParameters search, ref BluetoothDeviceInfo device);

    [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr find, ref BluetoothDeviceInfo device);

    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr find);

}
