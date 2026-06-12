using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Fluid.Service;

/// <summary>
/// v1.25: reads NVMe drive temperature with ZERO kernel driver of our own.
///
/// Uses the documented StorNVMe path: open the physical drive handle, then
/// DeviceIoControl(IOCTL_STORAGE_QUERY_PROPERTY) with
/// StorageDeviceProtocolSpecificProperty / NVMeDataTypeLogPage to pull the
/// SMART/Health Information log (log page 0x02). Byte offsets 1-2 of that log
/// hold "Composite Temperature" in Kelvin. This is pure user-mode Win32 — the
/// same mechanism Task Manager and CrystalDiskInfo use — and needs only the
/// elevation the service already runs with. No PawnIO, no WinRing0.
///
/// Returns null for non-NVMe disks (SATA SSDs/HDDs don't answer this protocol),
/// missing drives, or any failure. Callers treat null as "no temp".
/// </summary>
internal static class NvmeTemperature
{
    public static float? Read(string physicalDiskIndex, ILogger log)
    {
        if (string.IsNullOrEmpty(physicalDiskIndex)) return null;
        if (!int.TryParse(physicalDiskIndex, out var idx)) return null;

        SafeFileHandle? handle = null;
        try
        {
            handle = CreateFile(
                $@"\\.\PhysicalDrive{idx}",
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
                return null; // can't open (e.g. needs elevation we lack) — silent

            return QueryNvmeTemperature(handle, log);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "NVMe temperature read failed for disk {Index}", idx);
            return null;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    private static unsafe float? QueryNvmeTemperature(SafeFileHandle handle, ILogger log)
    {
        // STORAGE_PROPERTY_QUERY header + STORAGE_PROTOCOL_SPECIFIC_DATA, then
        // a 512-byte log page buffer. We size one contiguous buffer for the
        // whole request/response (the API returns the protocol data + log page
        // inline after the headers).
        const int headerSize = 48;          // query(8) + protocol-specific(40)
        const int logPageSize = 512;        // NVMe SMART/Health log is 512 bytes
        const int bufferSize = headerSize + logPageSize;

        var buffer = new byte[bufferSize];
        fixed (byte* p = buffer)
        {
            // --- STORAGE_PROPERTY_QUERY ---
            // PropertyId = StorageDeviceProtocolSpecificProperty (50)
            // QueryType  = PropertyStandardQuery (0)
            *(int*)(p + 0) = 50;
            *(int*)(p + 4) = 0;

            // --- STORAGE_PROTOCOL_SPECIFIC_DATA (starts at offset 8) ---
            byte* psd = p + 8;
            *(int*)(psd + 0)  = 3;          // ProtocolType = ProtocolTypeNvme
            *(int*)(psd + 4)  = 2;          // DataType = NVMeDataTypeLogPage
            *(int*)(psd + 8)  = 0x02;       // RequestValue = SMART/Health log page
            *(int*)(psd + 12) = 0;          // RequestSubValue
            *(int*)(psd + 16) = headerSize; // ProtocolDataOffset (from start of PSD)
            *(int*)(psd + 20) = logPageSize;// ProtocolDataLength

            uint bytesReturned;
            bool ok = DeviceIoControl(
                handle,
                IOCTL_STORAGE_QUERY_PROPERTY,
                (IntPtr)p, (uint)bufferSize,
                (IntPtr)p, (uint)bufferSize,
                out bytesReturned,
                IntPtr.Zero);

            if (!ok) return null;

            // The SMART/Health log page begins at headerSize. Composite
            // Temperature is a little-endian uint16 at log byte offset 1-2,
            // expressed in Kelvin.
            byte* logPage = p + headerSize;
            int kelvin = logPage[1] | (logPage[2] << 8);
            if (kelvin <= 0) return null;

            float celsius = kelvin - 273.15f;
            // Sanity clamp: reject obviously bad reads (some controllers return
            // 0 or wild values when the field is unsupported).
            if (celsius < -40f || celsius > 125f) return null;
            return celsius;
        }
    }

    // ---- Win32 interop ----
    private const uint GENERIC_READ  = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ  = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    // IOCTL_STORAGE_QUERY_PROPERTY = CTL_CODE(IOCTL_STORAGE_BASE, 0x0500, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);
}
