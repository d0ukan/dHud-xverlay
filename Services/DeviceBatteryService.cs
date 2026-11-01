using System.Runtime.InteropServices;

namespace DHud.Services;

/// <summary>Pili okunabilen bir cihaz (Bluetooth kulaklık, mouse, klavye, kol vb.).</summary>
public sealed record DeviceBattery(string Name, int Percent);

/// <summary>
/// Windows'un cihaz düğümlerinde tuttuğu pil düzeyini okur (Ayarlar → Bluetooth ve cihazlar'da görünen değerin aynısı).
/// Yalnızca Windows'un pil bilgisi sunan cihazlar listelenir; her cihaz bunu sağlamaz (ör. 2,4 GHz alıcılı bazı mouse'lar).
/// </summary>
public static class DeviceBatteryService
{
    // DEVPKEY_Device_BatteryLevel (Bluetooth cihazlarının pil yüzdesi, tür: bayt).
    private static readonly DevPropKey BatteryLevel = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    private static readonly DevPropKey FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly DevPropKey DeviceDesc = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);

    private const uint DigcfPresent = 0x2;
    private const uint DigcfAllClasses = 0x4;
    private const uint TypeByte = 0x3;
    private const int MaxDevices = 4096;

    /// <summary>Takılı ve pil düzeyi bildirilen cihazlar (ada göre tekil, en düşük pil önde). Hata olursa boş liste.</summary>
    public static List<DeviceBattery> Read()
    {
        var result = new Dictionary<string, DeviceBattery>(StringComparer.OrdinalIgnoreCase);
        IntPtr set;
        try
        {
            set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return [];
        }

        if (set == new IntPtr(-1))
        {
            return [];
        }

        try
        {
            var info = new DevInfoData { Size = (uint)Marshal.SizeOf<DevInfoData>() };
            for (uint index = 0; index < MaxDevices && SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                var level = ReadBytes(set, ref info, BatteryLevel);
                if (level is not { Length: > 0 } || level[0] > 100)
                {
                    continue;
                }

                var name = ReadString(set, ref info, FriendlyName) ?? ReadString(set, ref info, DeviceDesc);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                name = name.Trim();
                if (!result.TryGetValue(name, out var existing) || level[0] < existing.Percent)
                {
                    result[name] = new DeviceBattery(name, level[0]);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result.Values.OrderBy(d => d.Percent).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static byte[]? ReadBytes(IntPtr set, ref DevInfoData info, DevPropKey key)
    {
        var buffer = new byte[4];
        return SetupDiGetDevicePropertyW(set, ref info, ref key, out var type, buffer, (uint)buffer.Length, out var needed, 0) && type == TypeByte && needed >= 1
            ? buffer[..Math.Min((int)needed, buffer.Length)]
            : null;
    }

    private static string? ReadString(IntPtr set, ref DevInfoData info, DevPropKey key)
    {
        var buffer = new byte[512];
        if (!SetupDiGetDevicePropertyW(set, ref info, ref key, out _, buffer, (uint)buffer.Length, out var needed, 0) || needed < 2)
        {
            return null;
        }

        return System.Text.Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(needed, buffer.Length)).TrimEnd('\0');
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DevPropKey(Guid id, uint pid)
    {
        public readonly Guid Id = id;
        public readonly uint Pid = pid;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DevInfoData data);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref DevInfoData data, ref DevPropKey key, out uint type,
        byte[] buffer, uint size, out uint required, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
