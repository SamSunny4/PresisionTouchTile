using System.Runtime.InteropServices;

namespace TouchpadVisualizer.Input;

/// <summary>
/// P/Invoke declarations for Raw Input API and HID parsing functions.
/// These are required to capture precision touchpad contacts at the lowest level.
/// </summary>
internal static class HidInterop
{
    // ─── Raw Input Registration ────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList(
        [Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    // ─── HID Parsing Functions ─────────────────────────────────────────

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetCaps(
        IntPtr PreparsedData,
        out HIDP_CAPS Capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetValueCaps(
        HIDP_REPORT_TYPE ReportType,
        [Out] HIDP_VALUE_CAPS[] ValueCaps,
        ref ushort ValueCapsLength,
        IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetUsageValue(
        HIDP_REPORT_TYPE ReportType,
        ushort UsagePage,
        ushort LinkCollection,
        ushort Usage,
        out uint UsageValue,
        IntPtr PreparsedData,
        IntPtr Report,
        uint ReportLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetButtonCaps(
        HIDP_REPORT_TYPE ReportType,
        [Out] HIDP_BUTTON_CAPS[] ButtonCaps,
        ref ushort ButtonCapsLength,
        IntPtr PreparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetUsages(
        HIDP_REPORT_TYPE ReportType,
        ushort UsagePage,
        ushort LinkCollection,
        [Out] ushort[] UsageList,
        ref uint UsageLength,
        IntPtr PreparsedData,
        IntPtr Report,
        uint ReportLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetLinkCollectionNodes(
        [Out] HIDP_LINK_COLLECTION_NODE[] LinkCollectionNodes,
        ref uint LinkCollectionNodesLength,
        IntPtr PreparsedData);

    // ─── Gesture / Touch / Pointer Suppression ────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterTouchWindow(IntPtr hWnd, uint ulFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterTouchWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseTouchInputHandle(IntPtr hTouchInput);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ─── Constants ─────────────────────────────────────────────────────

    public const int WM_INPUT = 0x00FF;
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;

    // Gesture / Touch / Pointer messages to suppress
    public const int WM_GESTURE = 0x0119;
    public const int WM_GESTURENOTIFY = 0x011A;
    public const int WM_TOUCH = 0x0240;
    public const int WM_POINTERUPDATE = 0x0245;
    public const int WM_POINTERDOWN = 0x0246;
    public const int WM_POINTERUP = 0x0247;
    public const int WM_POINTERENTER = 0x0249;
    public const int WM_POINTERLEAVE = 0x024A;
    public const int WM_POINTERACTIVATE = 0x024B;
    public const int WM_POINTERCAPTURECHANGED = 0x024C;
    public const int WM_POINTERWHEEL = 0x024E;
    public const int WM_POINTERHWHEEL = 0x024F;

    public const uint TWF_WANTPALM = 0x00000002;

    public const uint RID_INPUT = 0x10000003;
    public const uint RID_HEADER = 0x10000005;
    public const uint RIDI_PREPARSEDDATA = 0x20000005;
    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_DEVICEINFO = 0x2000000b;

    public const uint RIM_TYPEMOUSE = 0;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RIM_TYPEHID = 2;

    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_REMOVE = 0x00000001;
    public const uint RIDEV_PAGEONLY = 0x00000020;

    // HID Usage Pages
    public const ushort HID_USAGE_PAGE_DIGITIZER = 0x0D;
    public const ushort HID_USAGE_PAGE_GENERIC = 0x01;

    // HID Usages for Digitizers
    public const ushort HID_USAGE_DIGITIZER_TOUCH_PAD = 0x05;
    public const ushort HID_USAGE_DIGITIZER_FINGER = 0x22;
    public const ushort HID_USAGE_DIGITIZER_TIP_SWITCH = 0x42;
    public const ushort HID_USAGE_DIGITIZER_CONTACT_ID = 0x51;
    public const ushort HID_USAGE_DIGITIZER_CONTACT_COUNT = 0x54;
    public const ushort HID_USAGE_DIGITIZER_X = 0x30; // Generic Desktop X
    public const ushort HID_USAGE_DIGITIZER_Y = 0x31; // Generic Desktop Y

    // NTSTATUS codes
    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    // ─── Structures ────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWHID
    {
        public uint dwSizeHid;
        public uint dwCount;
        // bRawData follows — variable length
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWMOUSE
    {
        public ushort usFlags;
        public uint ulButtons;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    // Union of RAWMOUSE/RAWKEYBOARD/RAWHID - we use explicit layout
    [StructLayout(LayoutKind.Explicit)]
    public struct RAWINPUT_DATA
    {
        [FieldOffset(0)] public RAWMOUSE mouse;
        [FieldOffset(0)] public RAWKEYBOARD keyboard;
        [FieldOffset(0)] public RAWHID hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    public enum HIDP_REPORT_TYPE
    {
        HidP_Input = 0,
        HidP_Output = 1,
        HidP_Feature = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsAlias;

        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsRange;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsStringRange;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsDesignatorRange;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsAbsolute;

        [MarshalAs(UnmanagedType.U1)]
        public bool HasNull;

        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] Reserved2;

        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;

        // Union: Range or NotRange
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_BUTTON_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsAlias;

        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsRange;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsStringRange;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsDesignatorRange;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsAbsolute;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public uint[] Reserved;

        // Union
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_LINK_COLLECTION_NODE
    {
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public ushort Parent;
        public ushort NumberOfChildren;
        public ushort NextSibling;
        public ushort FirstChild;
        public uint CollectionType_and_IsAlias; // packed bitfield
        public IntPtr UserContext;
    }
}
