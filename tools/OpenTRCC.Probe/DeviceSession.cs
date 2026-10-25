using OpenTRCC.Devices;

namespace OpenTRCC.Probe;

internal enum TransportKind
{
    Auto = 0,
    Hid = 1,
    WinUsb = 2,
}

/// <summary>Owns the exclusive device handle for the duration of a command.</summary>
internal sealed class DeviceSession : IDisposable
{
    private DeviceSession(string kind, ILcdTransport transport, ThermalrightType2 device, LcdInterface @interface)
    {
        Kind = kind;
        Transport = transport;
        Device = device;
        Interface = @interface;
    }

    /// <summary>"hid" or "winusb".</summary>
    public string Kind { get; }

    public ILcdTransport Transport { get; }

    public ThermalrightType2 Device { get; }

    public LcdInterface Interface { get; }

    public static DeviceSession Open(CommandLine line)
    {
        TransportKind requested = ParseKind(line.Get("transport") ?? "auto");

        if (requested is TransportKind.Auto or TransportKind.Hid)
        {
            IReadOnlyList<LcdInterface> hidTargets = DeviceEnumerator.FindHidTargets();
            if (hidTargets.Count > 0)
            {
                LcdInterface chosenHid = Select(hidTargets, line);
                byte reportId = (byte)line.GetInt("report-id", 0);
                int pipeline = line.GetInt("pipeline", 2);
                HidTransport hid = HidTransport.Open(chosenHid.Path, reportId, pipeline);
                return new DeviceSession("hid", hid, new ThermalrightType2(hid), chosenHid);
            }

            if (requested == TransportKind.Hid)
            {
                throw new DeviceNotFoundException("No 0416:5302 MI_00 vendor HID interface found.");
            }
        }

        IReadOnlyList<LcdInterface> usbTargets = DeviceEnumerator.FindTargets();
        if (usbTargets.Count == 0)
        {
            throw new DeviceNotFoundException(
                "No 0416:5302 MI_00 HID interface and no 0416:5302 MI_01 WinUSB interface. " +
                "Check the 9-pin cable and close TRCC.");
        }

        LcdInterface chosen = Select(usbTargets, line);
        WinUsbTransport usb = WinUsbTransport.Open(chosen.Path);
        return new DeviceSession("winusb", usb, new ThermalrightType2(usb), chosen);
    }

    public void Dispose() => Device.Dispose();

    private static LcdInterface Select(IReadOnlyList<LcdInterface> targets, CommandLine line)
    {
        string? filter = line.Get("interface");
        if (!string.IsNullOrWhiteSpace(filter))
        {
            LcdInterface? match = targets.FirstOrDefault(
                t => t.Path.Contains(filter, StringComparison.OrdinalIgnoreCase));
            return match ?? throw new DeviceNotFoundException($"No interface path contains '{filter}'.");
        }

        int index = line.GetInt("index", 0);
        if (index < 0 || index >= targets.Count)
        {
            throw new DeviceNotFoundException($"Device index {index} is out of range (found {targets.Count}).");
        }

        return targets[index];
    }

    private static TransportKind ParseKind(string text) => text.ToLowerInvariant() switch
    {
        "auto" => TransportKind.Auto,
        "hid" => TransportKind.Hid,
        "winusb" or "usb" => TransportKind.WinUsb,
        _ => throw new ArgumentException("--transport must be auto, hid or winusb."),
    };
}
