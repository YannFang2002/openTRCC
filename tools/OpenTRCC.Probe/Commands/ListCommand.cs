using OpenTRCC.Devices;

namespace OpenTRCC.Probe.Commands;

internal static class ListCommand
{
    public static int Run(CommandLine line)
    {
        IReadOnlyList<LcdInterface> hidTargets = DeviceEnumerator.FindHidTargets();
        IReadOnlyList<LcdInterface> usbTargets = DeviceEnumerator.FindTargets();
        IReadOnlyList<int> trcc = DeviceEnumerator.FindTrccProcessIds();

        Console.WriteLine("== OpenTRCC probe list ==");
        Console.WriteLine($"0416:5302 MI_00 (HID, frame channel): {hidTargets.Count}");
        Console.WriteLine($"0416:5302 MI_01 (WinUSB):            {usbTargets.Count}");

        for (int i = 0; i < hidTargets.Count; i++)
        {
            LcdInterface target = hidTargets[i];
            Console.WriteLine();
            Console.WriteLine($"[hid {i}] {target.Path}");
            Console.WriteLine($"    instance: {target.InstanceId}");
            Console.Write("    state:    ");
            try
            {
                using HidTransport probe = HidTransport.Open(target.Path);
                Console.WriteLine(
                    $"available (usage page 0x{probe.UsagePage:X4}, out {probe.OutputReportLength} B, " +
                    $"in {probe.InputReportLength} B, payload {probe.PayloadSize} B)");
            }
            catch (DeviceBusyException ex)
            {
                Console.WriteLine($"BUSY - {ex.Message}");
            }
            catch (DeviceAccessDeniedException ex)
            {
                Console.WriteLine($"ACCESS DENIED - {ex.Message}");
            }
            catch (LcdDeviceException ex)
            {
                Console.WriteLine($"error - {ex.Message}");
            }
        }

        for (int i = 0; i < usbTargets.Count; i++)
        {
            LcdInterface target = usbTargets[i];
            Console.WriteLine();
            Console.WriteLine($"[usb {i}] {target.Path}");
            Console.WriteLine($"    instance: {target.InstanceId}");
            Console.WriteLine($"    driver:   {target.DriverService ?? "<unreadable>"}");
            Console.Write("    state:    ");
            try
            {
                using WinUsbTransport probe = WinUsbTransport.Open(target.Path);
                Console.WriteLine($"available (OUT packet {probe.OutPacketSize} B, IN packet {probe.InPacketSize} B)");
            }
            catch (LcdDeviceException ex)
            {
                Console.WriteLine($"unavailable - {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(trcc.Count == 0
            ? "TRCC: not running"
            : $"TRCC: running (PID {string.Join(", ", trcc)}) - close it before handshake/pattern/bench");

        if (hidTargets.Count == 0 && usbTargets.Count == 0)
        {
            Console.Error.WriteLine("No target interface found.");
            return ProbeExit.NotFound;
        }

        return ProbeExit.Ok;
    }
}
