using System.Runtime.InteropServices;

namespace ForzaG29Leds;

// Controls Logitech G29 shift indicator LEDs via raw USB HID output report.
//
// Protocol — from logitech-g29 npm package (nightmode/logitech-g29) and
//             Linux hid-lg4ff.c lg4ff_set_leds():
//
//   Data (7 bytes):  [0xF8, 0x12, ledMask, 0x00, 0x00, 0x00, 0x01]
//   On Windows the HID driver requires a Report-ID byte prepended (0x00 when
//   the device has no numbered reports), so WriteFile receives 8 bytes:
//   [0x00, 0xF8, 0x12, ledMask, 0x00, 0x00, 0x00, 0x01]
//
//   ledMask bits 0-4 map to LEDs 1-5 (bit 0 = first green, bit 4 = last red).
//
// Device selection — matches the node-hid findWheel() heuristic:
//   VendorId = 0x046D (Logitech)
//   ProductId = 0xC24F  OR  product name contains "G29"
//   HID Usage Page = 1 (Generic Desktop), i.e. the main joystick interface
internal sealed class G29HidLeds : IDisposable
{
    private static readonly IntPtr Invalid = new IntPtr(-1);
    private const ushort LogiVid = 0x046D;
    private const ushort G29Pid = 0xC24F;

    private IntPtr _handle;
    private int _reportLen;   // OutputReportByteLength (includes Report ID byte)
    private bool _disposed;

    private G29HidLeds(IntPtr h, int reportLen) { _handle = h; _reportLen = reportLen; }

    public bool IsOpen => _handle != Invalid && _handle != IntPtr.Zero;

    // ── Factory ───────────────────────────────────────────────────────────────

    public static G29HidLeds? Open()
    {
        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr devSet = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero,
                            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devSet == Invalid) return null;

        try
        {
            var iface = new SP_DEVICE_INTERFACE_DATA
            {
                cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
            };

            for (int i = 0;
                 SetupDiEnumDeviceInterfaces(devSet, IntPtr.Zero, ref hidGuid, i, ref iface);
                 i++)
            {
                SetupDiGetDeviceInterfaceDetail(devSet, ref iface,
                    IntPtr.Zero, 0, out int reqSize, IntPtr.Zero);

                IntPtr buf = Marshal.AllocHGlobal(reqSize);
                try
                {
                    // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA is 8 on x64, 6 on x86
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(devSet, ref iface,
                            buf, reqSize, out _, IntPtr.Zero))
                        continue;

                    string path = Marshal.PtrToStringUni(buf + 4) ?? "";
                    if (!path.Contains($"VID_{LogiVid:X4}", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!path.Contains($"PID_{G29Pid:X4}", StringComparison.OrdinalIgnoreCase))
                        continue;

                    IntPtr h = CreateFile(path,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                    if (h == Invalid || h == IntPtr.Zero) continue;

                    if (!HidD_GetPreparsedData(h, out IntPtr prep))
                    {
                        CloseHandle(h);
                        continue;
                    }

                    HIDP_CAPS caps = default;
                    HidP_GetCaps(prep, ref caps);
                    HidD_FreePreparsedData(prep);

                    // UsagePage 1 = Generic Desktop — the main wheel interface that accepts LED reports
                    if (caps.UsagePage != 1)
                    {
                        CloseHandle(h);
                        continue;
                    }

                    return new G29HidLeds(h, caps.OutputReportByteLength);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devSet); }

        return null;
    }

    // ── LED control ───────────────────────────────────────────────────────────

    // ratio: current RPM / max RPM  (0.0 – 1.0)
    public bool SetFromRatio(float ratio) => WriteReport(RatioToMask(ratio));

    // Maps 0.0–1.0 to a 5-bit LED mask (bit 0 = first LED, bit 4 = last).
    internal static byte RatioToMask(float ratio)
    {
        int lit = Math.Clamp((int)Math.Ceiling(ratio * 5f), 0, 5);
        return (byte)((1 << lit) - 1);   // 0b00001 → 0b11111
    }

    public bool TurnOff() => WriteReport(0x00);
    public bool AllOn() => WriteReport(0x1F);

    private bool WriteReport(byte ledMask)
    {
        if (!IsOpen) return false;

        // Windows HID WriteFile requires exactly OutputReportByteLength bytes.
        // byte[0]  = 0x00 (Report ID — devices with no numbered reports use 0)
        // bytes[1..7] = the 7-byte LED command from hid-lg4ff / logitech-g29 npm
        // remainder   = 0x00 padding to reach OutputReportByteLength
        int len = Math.Max(_reportLen, 8);
        byte[] buf = new byte[len];
        buf[0] = 0x00;   // Report ID
        buf[1] = 0xF8;
        buf[2] = 0x12;
        buf[3] = ledMask;
        buf[7] = 0x01;

        bool ok = WriteFile(_handle, buf, (uint)buf.Length, out uint written, IntPtr.Zero);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            // Device was physically disconnected — invalidate handle so reconnect loop picks it up
            if (err is 1167 or 433 or 6) // ERROR_DEVICE_NOT_CONNECTED, ERROR_NO_SUCH_DEVICE, ERROR_INVALID_HANDLE
            {
                CloseHandle(_handle);
                _handle = Invalid;
            }
        }
        return ok;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (IsOpen) CloseHandle(_handle);
            _handle = Invalid;
            _disposed = true;
        }
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid HidGuid);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr dev, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, string? Enumerator, IntPtr hwnd, int Flags);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr Set, IntPtr DevInfo, ref Guid Guid, int Index, ref SP_DEVICE_INTERFACE_DATA Data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr Set, ref SP_DEVICE_INTERFACE_DATA Data, IntPtr Detail, int Size, out int Required, IntPtr DevInfo);
    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr Set);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr CreateFile(string name, uint access, uint share,
        IntPtr sec, uint disposition, uint flags, IntPtr templ);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool WriteFile(IntPtr h, byte[] buf, uint count, out uint written, IntPtr overlapped);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    private const int DIGCF_PRESENT = 0x02;
    private const int DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
}
