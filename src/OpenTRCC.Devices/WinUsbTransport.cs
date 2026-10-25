using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenTRCC.Devices.Interop;

namespace OpenTRCC.Devices;

/// <summary>
/// WinUSB bulk transport. Opens the MI_01 device interface exclusively so a
/// running TRCC is detected immediately instead of via a handshake timeout.
/// </summary>
public sealed class WinUsbTransport : ILcdTransport
{
    public const byte OutPipeId = 0x02;
    public const byte InPipeId = 0x01;

    private nint _fileHandle;    private nint _winUsbHandle;
    private bool _disposed;

    private WinUsbTransport(string interfacePath, nint fileHandle, nint winUsbHandle)
    {
        Description = interfacePath;
        _fileHandle = fileHandle;
        _winUsbHandle = winUsbHandle;
    }

    public string Description { get; }

    public ushort OutPacketSize { get; private init; }

    public ushort InPacketSize { get; private init; }

    /// <summary>Opens the interface read/write and initializes WinUSB.</summary>
    public static WinUsbTransport Open(string interfacePath)
    {
        nint handle = NativeMethods.CreateFile(
            interfacePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareNone,
            nint.Zero,
            NativeMethods.OpenExisting,
            0,
            nint.Zero);

        if (handle == new nint(-1))
        {
            int error = Marshal.GetLastWin32Error();
            switch (error)
            {
                case NativeMethods.ErrorSharingViolation:
                    IReadOnlyList<int> pids = DeviceEnumerator.FindTrccProcessIds();
                    string who = pids.Count > 0
                        ? $"TRCC (PID {string.Join(", ", pids)})"
                        : "another process";
                    throw new DeviceBusyException(
                        $"{who} is holding the LCD device exclusively. Close it and retry.", pids);

                case NativeMethods.ErrorAccessDenied:
                    throw new DeviceAccessDeniedException(
                        "WinUSB refused access to the device (ERROR_ACCESS_DENIED). Restart OpenTRCC elevated once.");

                default:
                    throw new LcdDeviceException(
                        $"CreateFile failed for '{interfacePath}': {new Win32Exception(error).Message}");
            }
        }

        if (NativeMethods.WinUsb_Initialize(handle, out nint winUsb) == 0)
        {
            int error = Marshal.GetLastWin32Error();
            NativeMethods.CloseHandle(handle);
            if (error == NativeMethods.ErrorAccessDenied)
            {
                throw new DeviceAccessDeniedException(
                    "WinUsb_Initialize failed with ERROR_ACCESS_DENIED; the device is not bound to WinUSB.");
            }

            throw new LcdDeviceException($"WinUsb_Initialize failed: {new Win32Exception(error).Message}");
        }

        try
        {
            if (NativeMethods.WinUsb_QueryInterfaceSettings(winUsb, 0, out NativeMethods.UsbInterfaceDescriptor descriptor) == 0)
            {
                throw new LcdDeviceException(
                    $"WinUsb_QueryInterfaceSettings failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }

            ushort outSize = 0;
            ushort inSize = 0;

            for (byte index = 0; index < descriptor.NumEndpoints; index++)
            {
                if (NativeMethods.WinUsb_QueryPipe(winUsb, 0, index, out NativeMethods.WinUsbPipeInformation pipe) == 0)
                {
                    throw new LcdDeviceException(
                        $"WinUsb_QueryPipe({index}) failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                }

                if (pipe.PipeType != NativeMethods.UsbdPipeTypeBulk)
                {
                    continue;
                }

                if (pipe.EndpointId == OutPipeId)
                {
                    outSize = pipe.MaximumPacketSize;
                }
                else if (pipe.EndpointId == InPipeId)
                {
                    inSize = pipe.MaximumPacketSize;
                }
            }

            if (outSize == 0)
            {
                throw new LcdDeviceException("Bulk OUT pipe 0x02 not found on the device.");
            }

            if (inSize == 0)
            {
                throw new LcdDeviceException("Bulk IN pipe 0x01 not found on the device; handshake impossible.");
            }

            NativeMethods.SetTimeout(winUsb, OutPipeId, NativeMethods.WriteTimeoutMs);
            NativeMethods.SetTimeout(winUsb, InPipeId, NativeMethods.ReadTimeoutMs);

            return new WinUsbTransport(interfacePath, handle, winUsb)
            {
                OutPacketSize = outSize,
                InPacketSize = inSize,
            };
        }
        catch
        {
            NativeMethods.WinUsb_Free(winUsb);
            NativeMethods.CloseHandle(handle);
            throw;
        }
    }

    /// <inheritdoc />
    public unsafe void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (data.IsEmpty)
        {
            return;
        }

        fixed (byte* pointer = data)
        {
            if (NativeMethods.WinUsb_WritePipe(
                    _winUsbHandle, OutPipeId, pointer, (uint)data.Length,
                    out uint transferred, nint.Zero) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                throw error switch
                {
                    NativeMethods.ErrorSemTimeout => new LcdTimeoutException(
                        $"Write timed out after {data.Length} bytes."),
                    NativeMethods.ErrorOperationAborted => new LcdDeviceException("Write aborted; device disappeared."),
                    _ => new LcdDeviceException($"WinUsb_WritePipe failed: {new Win32Exception(error).Message}"),
                };
            }

            if (transferred != data.Length)
            {
                throw new ShortWriteException(data.Length, (int)transferred);
            }
        }
    }

    /// <inheritdoc />
    public unsafe int Read(Span<byte> buffer, TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint timeoutMs = (uint)Math.Clamp(timeout.TotalMilliseconds, 1, 60_000);
        NativeMethods.SetTimeout(_winUsbHandle, InPipeId, timeoutMs);

        fixed (byte* pointer = buffer)
        {
            if (NativeMethods.WinUsb_ReadPipe(
                    _winUsbHandle, InPipeId, pointer, (uint)buffer.Length,
                    out uint transferred, nint.Zero) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                throw error switch
                {
                    NativeMethods.ErrorSemTimeout => new LcdTimeoutException(
                        $"Read timed out after {timeoutMs} ms."),
                    NativeMethods.ErrorOperationAborted => new LcdDeviceException("Read aborted; device disappeared."),
                    _ => new LcdDeviceException($"WinUsb_ReadPipe failed: {new Win32Exception(error).Message}"),
                };
            }

            return (int)transferred;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_winUsbHandle != nint.Zero)
        {
            NativeMethods.WinUsb_AbortPipe(_winUsbHandle, OutPipeId);
            NativeMethods.WinUsb_Free(_winUsbHandle);
            _winUsbHandle = nint.Zero;
        }

        if (_fileHandle != nint.Zero)
        {
            NativeMethods.CloseHandle(_fileHandle);
            _fileHandle = nint.Zero;
        }
    }
}
