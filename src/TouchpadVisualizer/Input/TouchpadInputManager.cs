using System.Diagnostics;
using System.Runtime.InteropServices;
using TouchpadVisualizer.Models;
using static TouchpadVisualizer.Input.HidInterop;

namespace TouchpadVisualizer.Input;

/// <summary>
/// Event arguments for touchpad contact events.
/// </summary>
public class TouchpadContactEventArgs : EventArgs
{
    public required TouchContact[] Contacts { get; init; }
}

/// <summary>
/// Manages raw input registration and parsing for Windows Precision Touchpads.
/// Registers for HID raw input (Usage Page 0x0D, Usage 0x05) and parses
/// individual finger contacts from HID reports.
///
/// Supports both parallel and serial (hybrid) reporting modes.
/// </summary>
public sealed class TouchpadInputManager : IDisposable
{
    // Per-device parsed descriptor info
    private class DeviceInfo
    {
        public IntPtr PreparsedData;
        public int PreparsedDataSize;
        public ushort InputReportByteLength;
        public int MaxContactCount;

        // Link collections for each finger slot (may be all the same in serial mode)
        public ushort[] FingerLinkCollections = [];

        // Whether each finger collection has its own unique link collection number
        // (parallel mode) or they all share the same one (serial mode)
        public bool IsSerialMode;

        // Value ranges for normalization
        public int XLogicalMin, XLogicalMax;
        public int YLogicalMin, YLogicalMax;
    }

    private readonly Dictionary<IntPtr, DeviceInfo> _devices = new();
    private readonly Dictionary<int, TouchContact> _activeContacts = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private bool _isRegistered;
    private bool _disposed;

    // Pending contacts accumulation for serial mode
    private readonly List<TouchContact> _pendingContacts = new();

    // Calibration overrides
    private bool _useCalibration;
    private int _calXMin, _calXMax, _calYMin, _calYMax;

    /// <summary>Raised when touchpad contacts are updated.</summary>
    public event EventHandler<TouchpadContactEventArgs>? ContactsUpdated;

    /// <summary>Raised when a finger first touches the pad.</summary>
    public event EventHandler<TouchContact>? TouchDown;

    /// <summary>Raised when a finger lifts from the pad.</summary>
    public event EventHandler<TouchContact>? TouchUp;

    /// <summary>
    /// Sets physical calibration bounds to override the hardware's reported logical capabilities.
    /// Call with useCalibration=false to revert to hardware defaults.
    /// </summary>
    public void SetCalibration(bool useCalibration, int xMin, int xMax, int yMin, int yMax)
    {
        _useCalibration = useCalibration;
        _calXMin = xMin;
        _calXMax = xMax;
        _calYMin = yMin;
        _calYMax = yMax;
    }

    /// <summary>
    /// Register for raw touchpad input on the given window.
    /// Must be called after the window handle is available.
    /// </summary>
    public bool Register(IntPtr hwnd)
    {
        if (_isRegistered) return true;

        var device = new RAWINPUTDEVICE
        {
            UsagePage = HID_USAGE_PAGE_DIGITIZER,
            Usage = HID_USAGE_DIGITIZER_TOUCH_PAD,
            Flags = RIDEV_INPUTSINK,
            Target = hwnd
        };

        bool result = RegisterRawInputDevices(
            [device],
            1,
            (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

        _isRegistered = result;

        if (result)
            Debug.WriteLine("[TouchpadInput] Registered for raw touchpad input.");
        else
            Debug.WriteLine($"[TouchpadInput] Registration failed. Error: {Marshal.GetLastWin32Error()}");

        return result;
    }

    /// <summary>Unregister from raw touchpad input.</summary>
    public void Unregister()
    {
        if (!_isRegistered) return;

        var device = new RAWINPUTDEVICE
        {
            UsagePage = HID_USAGE_PAGE_DIGITIZER,
            Usage = HID_USAGE_DIGITIZER_TOUCH_PAD,
            Flags = RIDEV_REMOVE,
            Target = IntPtr.Zero
        };

        RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        _isRegistered = false;
    }

    /// <summary>
    /// Process a WM_INPUT message. Call from WndProc.
    /// </summary>
    public bool ProcessRawInput(IntPtr lParam)
    {
        uint dataSize = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

        if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dataSize, headerSize) != 0)
            return false;
        if (dataSize == 0)
            return false;

        var buffer = Marshal.AllocHGlobal((int)dataSize);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref dataSize, headerSize) != dataSize)
                return false;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.dwType != RIM_TYPEHID)
                return false;

            var deviceInfo = GetOrCreateDeviceInfo(header.hDevice);
            if (deviceInfo == null)
                return false;

            var rawHidPtr = buffer + (int)headerSize;
            var rawHid = Marshal.PtrToStructure<RAWHID>(rawHidPtr);

            if (rawHid.dwSizeHid == 0 || rawHid.dwCount == 0)
                return false;

            var reportPtr = rawHidPtr + Marshal.SizeOf<RAWHID>();
            long timestamp = _stopwatch.ElapsedTicks;

            for (uint i = 0; i < rawHid.dwCount; i++)
            {
                var currentReportPtr = reportPtr + (int)(i * rawHid.dwSizeHid);
                ParseHidReport(deviceInfo, currentReportPtr, rawHid.dwSizeHid, timestamp);
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Parse one HID report. Handles both serial (one contact/message) and
    /// parallel (all contacts/message) modes.
    /// </summary>
    private void ParseHidReport(DeviceInfo device, IntPtr reportPtr, uint reportSize, long timestamp)
    {
        // Read contact count from the top-level collection (link collection 0)
        HidP_GetUsageValue(
            HIDP_REPORT_TYPE.HidP_Input,
            HID_USAGE_PAGE_DIGITIZER,
            0,
            HID_USAGE_DIGITIZER_CONTACT_COUNT,
            out uint reportContactCount,
            device.PreparsedData,
            reportPtr,
            reportSize);

        if (device.IsSerialMode)
        {
            // Serial mode: one contact per WM_INPUT.
            // The contact is always in FingerLinkCollections[0].
            // reportContactCount > 0 on the LAST message in the batch; it indicates total contacts.
            if (device.FingerLinkCollections.Length > 0)
            {
                var contact = ParseSingleContact(device, device.FingerLinkCollections[0], reportPtr, reportSize, timestamp);
                if (contact.HasValue)
                    _pendingContacts.Add(contact.Value);
            }

            if (reportContactCount > 0)
            {
                // This is the last (or only) report in this frame's batch. Flush now.
                FlushContacts(timestamp);
            }
            // else: intermediate report, accumulate and wait for the final one.
        }
        else
        {
            // Parallel mode: all contacts are in this single report, each in its own link collection.
            int totalContacts = reportContactCount > 0
                ? (int)reportContactCount
                : device.FingerLinkCollections.Length;

            int slotsToRead = Math.Min(totalContacts, device.FingerLinkCollections.Length);
            for (int i = 0; i < slotsToRead; i++)
            {
                var contact = ParseSingleContact(device, device.FingerLinkCollections[i], reportPtr, reportSize, timestamp);
                if (contact.HasValue)
                    _pendingContacts.Add(contact.Value);
            }
            FlushContacts(timestamp);
        }
    }

    /// <summary>
    /// Parse a single contact from the given HID report and link collection.
    /// Returns null if the contact data is absent or invalid.
    /// </summary>
    private TouchContact? ParseSingleContact(DeviceInfo device, ushort linkCollection,
        IntPtr reportPtr, uint reportSize, long timestamp)
    {
        // 1. Tip switch — try the specified link collection first, then collection 0 as fallback
        bool tipSwitch = ReadTipSwitch(device, linkCollection, reportPtr, reportSize);

        // 2. Contact ID — required for tracking
        int getIdResult = HidP_GetUsageValue(
            HIDP_REPORT_TYPE.HidP_Input,
            HID_USAGE_PAGE_DIGITIZER,
            linkCollection,
            HID_USAGE_DIGITIZER_CONTACT_ID,
            out uint contactId,
            device.PreparsedData,
            reportPtr,
            reportSize);

        if (getIdResult != HIDP_STATUS_SUCCESS)
        {
            // Also try with linkCollection=0 — some devices put contact ID at top level
            getIdResult = HidP_GetUsageValue(
                HIDP_REPORT_TYPE.HidP_Input,
                HID_USAGE_PAGE_DIGITIZER,
                0,
                HID_USAGE_DIGITIZER_CONTACT_ID,
                out contactId,
                device.PreparsedData,
                reportPtr,
                reportSize);

            if (getIdResult != HIDP_STATUS_SUCCESS)
                return null;
        }

        // 3. X coordinate
        HidP_GetUsageValue(
            HIDP_REPORT_TYPE.HidP_Input,
            HID_USAGE_PAGE_GENERIC,
            linkCollection,
            0x30, // X
            out uint rawX,
            device.PreparsedData,
            reportPtr,
            reportSize);

        // 4. Y coordinate
        HidP_GetUsageValue(
            HIDP_REPORT_TYPE.HidP_Input,
            HID_USAGE_PAGE_GENERIC,
            linkCollection,
            0x31, // Y
            out uint rawY,
            device.PreparsedData,
            reportPtr,
            reportSize);

        // 5. Normalize
        int minX = _useCalibration ? _calXMin : device.XLogicalMin;
        int maxX = _useCalibration ? _calXMax : device.XLogicalMax;
        int minY = _useCalibration ? _calYMin : device.YLogicalMin;
        int maxY = _useCalibration ? _calYMax : device.YLogicalMax;

        // The uint from HidP_GetUsageValue is the unsigned logical value.
        // Re-interpret as signed if the logical range implies signed (XLogicalMin < 0)
        int signedX = device.XLogicalMin < 0 ? (int)rawX : (int)(rawX);
        int signedY = device.YLogicalMin < 0 ? (int)rawY : (int)(rawY);

        float normX = maxX != minX ? (float)(signedX - minX) / (maxX - minX) : 0.5f;
        float normY = maxY != minY ? (float)(signedY - minY) / (maxY - minY) : 0.5f;

        normX = Math.Clamp(normX, 0f, 1f);
        normY = Math.Clamp(normY, 0f, 1f);

        return new TouchContact
        {
            ContactId = (int)contactId,
            RawX = signedX,
            RawY = signedY,
            IsDown = tipSwitch,
            NormalizedX = normX,
            NormalizedY = normY,
            Pressure = tipSwitch ? 1.0f : 0f,
            Timestamp = timestamp
        };
    }

    /// <summary>
    /// Read the tip switch (finger-down) boolean from a HID report.
    /// Tries the given link collection, then link collection 0 as fallback.
    /// </summary>
    private static bool ReadTipSwitch(DeviceInfo device, ushort linkCollection,
        IntPtr reportPtr, uint reportSize)
    {
        // Try the specific collection first
        uint usageCount = 16;
        var usages = new ushort[16];
        if (HidP_GetUsages(
            HIDP_REPORT_TYPE.HidP_Input,
            HID_USAGE_PAGE_DIGITIZER,
            linkCollection,
            usages,
            ref usageCount,
            device.PreparsedData,
            reportPtr,
            reportSize) == HIDP_STATUS_SUCCESS)
        {
            for (uint u = 0; u < usageCount; u++)
                if (usages[u] == HID_USAGE_DIGITIZER_TIP_SWITCH)
                    return true;
        }

        // Fallback: try top-level collection 0
        if (linkCollection != 0)
        {
            usageCount = 16;
            if (HidP_GetUsages(
                HIDP_REPORT_TYPE.HidP_Input,
                HID_USAGE_PAGE_DIGITIZER,
                0,
                usages,
                ref usageCount,
                device.PreparsedData,
                reportPtr,
                reportSize) == HIDP_STATUS_SUCCESS)
            {
                for (uint u = 0; u < usageCount; u++)
                    if (usages[u] == HID_USAGE_DIGITIZER_TIP_SWITCH)
                        return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Flush pending contacts: compute velocities, update state, fire events.
    /// </summary>
    private void FlushContacts(long timestamp)
    {
        foreach (var contact in _pendingContacts)
        {
            bool wasDown = _activeContacts.TryGetValue(contact.ContactId, out var prevContact);
            var updatedContact = contact;

            if (wasDown && contact.IsDown)
            {
                float dt = (float)(contact.Timestamp - prevContact.Timestamp) / Stopwatch.Frequency;
                if (dt > 0 && dt < 0.1f)
                {
                    updatedContact = contact with
                    {
                        VelocityX = (contact.NormalizedX - prevContact.NormalizedX) / dt,
                        VelocityY = (contact.NormalizedY - prevContact.NormalizedY) / dt
                    };
                }
            }

            if (updatedContact.IsDown)
            {
                if (!wasDown) TouchDown?.Invoke(this, updatedContact);
                _activeContacts[updatedContact.ContactId] = updatedContact;
            }
            else if (wasDown)
            {
                TouchUp?.Invoke(this, updatedContact);
                _activeContacts.Remove(updatedContact.ContactId);
            }
        }

        // In serial mode (1 contact/report), don't remove stale contacts —
        // other contacts will arrive in subsequent messages.
        // Only clean up stale contacts in parallel mode (all contacts in one batch).
        if (!_pendingContacts.Any(c => c.IsDown) || _pendingContacts.Count > 1)
        {
            var reportedIds = new HashSet<int>(_pendingContacts
                .Where(c => c.IsDown)
                .Select(c => c.ContactId));

            var staleIds = _activeContacts.Keys
                .Where(id => !reportedIds.Contains(id))
                .ToList();

            foreach (var id in staleIds)
            {
                var stale = _activeContacts[id];
                TouchUp?.Invoke(this, stale with { IsDown = false });
                _activeContacts.Remove(id);
            }
        }

        // Fire the event with ALL current active contacts
        ContactsUpdated?.Invoke(this, new TouchpadContactEventArgs
        {
            Contacts = [.. _activeContacts.Values]
        });

        _pendingContacts.Clear();
    }

    private DeviceInfo? GetOrCreateDeviceInfo(IntPtr hDevice)
    {
        if (_devices.TryGetValue(hDevice, out var info))
            return info;

        // Get preparsed data
        uint preparsedSize = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref preparsedSize);
        if (preparsedSize == 0) return null;

        var preparsedData = Marshal.AllocHGlobal((int)preparsedSize);
        GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, preparsedData, ref preparsedSize);

        if (HidP_GetCaps(preparsedData, out var caps) != HIDP_STATUS_SUCCESS)
        {
            Marshal.FreeHGlobal(preparsedData);
            return null;
        }

        info = new DeviceInfo
        {
            PreparsedData = preparsedData,
            PreparsedDataSize = (int)preparsedSize,
            InputReportByteLength = caps.InputReportByteLength
        };

        // === Discover finger link collections ===
        var fingerCollections = DiscoverFingerCollectionsViaNodes(preparsedData, caps);
        if (fingerCollections.Count == 0)
            fingerCollections = DiscoverFingerCollectionsViaValueCaps(preparsedData, caps);

        // === Parse coordinate ranges & max contacts from value caps ===
        ushort valueCapsCount = caps.NumberInputValueCaps;
        if (valueCapsCount > 0)
        {
            var valueCaps = new HIDP_VALUE_CAPS[valueCapsCount];
            HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input, valueCaps, ref valueCapsCount, preparsedData);

            int xMin = 0, xMax = 65535, yMin = 0, yMax = 65535;
            bool foundX = false, foundY = false;

            foreach (var vc in valueCaps)
            {
                ushort usage = vc.UsageMin;

                if (vc.UsagePage == HID_USAGE_PAGE_GENERIC)
                {
                    if (usage == 0x30 && !foundX)
                    {
                        xMin = vc.LogicalMin;
                        xMax = vc.LogicalMax;
                        foundX = true;
                    }
                    else if (usage == 0x31 && !foundY)
                    {
                        yMin = vc.LogicalMin;
                        yMax = vc.LogicalMax;
                        foundY = true;
                    }
                }

                if (vc.UsagePage == HID_USAGE_PAGE_DIGITIZER
                    && usage == HID_USAGE_DIGITIZER_CONTACT_COUNT
                    && vc.LinkCollection == 0)
                {
                    info.MaxContactCount = vc.LogicalMax;
                }
            }

            info.XLogicalMin = xMin;
            info.XLogicalMax = xMax;
            info.YLogicalMin = yMin;
            info.YLogicalMax = yMax;
        }

        info.FingerLinkCollections = fingerCollections.OrderBy(c => c).ToArray();
        if (info.MaxContactCount == 0)
            info.MaxContactCount = Math.Max(info.FingerLinkCollections.Length, 5);

        // If only 1 unique finger link collection exists but device reports
        // multiple contacts, this is serial mode.
        info.IsSerialMode = info.FingerLinkCollections.Length <= 1 && info.MaxContactCount > 1;

        // Default to collection 0 if nothing found — many touchpads expose everything there
        if (info.FingerLinkCollections.Length == 0)
            info.FingerLinkCollections = [1]; // collection 1 is the most common finger slot

        _devices[hDevice] = info;

        Debug.WriteLine($"[TouchpadInput] Device: Fingers={info.FingerLinkCollections.Length}, " +
                        $"SerialMode={info.IsSerialMode}, MaxContacts={info.MaxContactCount}, " +
                        $"X=[{info.XLogicalMin},{info.XLogicalMax}], Y=[{info.YLogicalMin},{info.YLogicalMax}], " +
                        $"Collections=[{string.Join(",", info.FingerLinkCollections)}]");

        return info;
    }

    private static HashSet<ushort> DiscoverFingerCollectionsViaNodes(IntPtr preparsedData, HIDP_CAPS caps)
    {
        var result = new HashSet<ushort>();
        uint nodeCount = caps.NumberLinkCollectionNodes;
        if (nodeCount == 0) return result;

        var nodes = new HIDP_LINK_COLLECTION_NODE[nodeCount];
        if (HidP_GetLinkCollectionNodes(nodes, ref nodeCount, preparsedData) != HIDP_STATUS_SUCCESS)
            return result;

        for (int i = 0; i < (int)nodeCount; i++)
        {
            if (nodes[i].LinkUsagePage == HID_USAGE_PAGE_DIGITIZER &&
                nodes[i].LinkUsage == HID_USAGE_DIGITIZER_FINGER)
            {
                result.Add((ushort)i);
            }
        }

        Debug.WriteLine($"[TouchpadInput] Link nodes: total={nodeCount}, fingerCollections={result.Count}");
        return result;
    }

    private static HashSet<ushort> DiscoverFingerCollectionsViaValueCaps(IntPtr preparsedData, HIDP_CAPS caps)
    {
        var result = new HashSet<ushort>();
        ushort valueCapsCount = caps.NumberInputValueCaps;
        if (valueCapsCount == 0) return result;

        var valueCaps = new HIDP_VALUE_CAPS[valueCapsCount];
        HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input, valueCaps, ref valueCapsCount, preparsedData);

        foreach (var vc in valueCaps)
        {
            // X usage in a non-root collection = finger slot
            if (vc.UsagePage == HID_USAGE_PAGE_GENERIC && vc.UsageMin == 0x30 && vc.LinkCollection > 0)
                result.Add(vc.LinkCollection);
        }

        // Also check button caps for tip-switch-bearing collections
        ushort buttonCapsCount = caps.NumberInputButtonCaps;
        if (buttonCapsCount > 0)
        {
            var buttonCaps = new HIDP_BUTTON_CAPS[buttonCapsCount];
            HidP_GetButtonCaps(HIDP_REPORT_TYPE.HidP_Input, buttonCaps, ref buttonCapsCount, preparsedData);
            foreach (var bc in buttonCaps)
            {
                if (bc.UsagePage == HID_USAGE_PAGE_DIGITIZER && bc.LinkCollection > 0)
                {
                    ushort uMin = bc.UsageMin;
                    ushort uMax = bc.IsRange ? bc.UsageMax : bc.UsageMin;
                    if (uMin <= HID_USAGE_DIGITIZER_TIP_SWITCH && uMax >= HID_USAGE_DIGITIZER_TIP_SWITCH)
                        result.Add(bc.LinkCollection);
                }
            }
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
        foreach (var kvp in _devices)
        {
            if (kvp.Value.PreparsedData != IntPtr.Zero)
                Marshal.FreeHGlobal(kvp.Value.PreparsedData);
        }
        _devices.Clear();
    }
}
