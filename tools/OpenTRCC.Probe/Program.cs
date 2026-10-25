using System.Text;
using OpenTRCC.Devices;
using OpenTRCC.Probe.Commands;

namespace OpenTRCC.Probe;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return ProbeExit.Ok;
        }

        CommandLine line;
        try
        {
            line = CommandLine.Parse(args.Skip(1).ToArray());
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"usage error: {ex.Message}");
            return ProbeExit.Usage;
        }

        string command = args[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "list" => ListCommand.Run(line),
                "handshake" => HandshakeCommand.Run(line),
                "pattern" => PatternCommand.Run(line),
                "bench" => BenchCommand.Run(line),
                "render" => RenderCommand.Run(line),
                "sensors" => SensorsCommand.Run(line),
                _ => PrintUsage(),
            };
        }
        catch (DeviceNotFoundException ex)
        {
            Fail(ex.Message);
            return ProbeExit.NotFound;
        }
        catch (DeviceBusyException ex)
        {
            Fail(ex.Message);
            return ProbeExit.Busy;
        }
        catch (DeviceAccessDeniedException ex)
        {
            Fail(ex.Message);
            return ProbeExit.AccessDenied;
        }
        catch (LcdDeviceException ex)
        {
            Fail(ex.Message);
            return ProbeExit.DeviceError;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("cancelled");
            return ProbeExit.Cancelled;
        }
    }

    private static void Fail(string message) => Console.Error.WriteLine($"error: {message}");

    private static int PrintUsage()
    {
        Console.WriteLine(
            """
            OpenTRCC.Probe - Thermalright 2.8" 320x240 (0416:5302) hardware probe

              probe list
                  Enumerate 0416:5302 MI_00 HID and MI_01 WinUSB interfaces, driver and occupancy.

              probe handshake [--mode auto|standard|streamed] [--out <dir>]
                  Run the 512-byte handshake, print PM/SUB, dump annotated hex.

              probe pattern [--mode rgb565|jpeg] [--rotate 0|90|180|270]
                            [--byte-order le|be] [--flip none|h|v|hv]
                            [--chunk-size 512] [--settle-ms 1] [--seconds 10]
                            [--save-png] [--save-profile] [--out <dir>]
                  Stream the diagnostic test image to the panel.

              probe bench [--all] [--chunk-size 512|1024|4096|full]
                          [--settle-ms 0|1] [--seconds 10] [--out <dir>]
                  Measure sustained frame rate and the firmware's chunk-size ceiling.

              probe render [--theme themes/pulse-320x240.json] [--png <path>]
                           [--push] [--seconds 10]
                  Render a theme to PNG and, with --push, stream it to the panel.

              probe sensors [--seconds 8] [--lhm] [--no-fps] [--interval 1000]
                  Sample system counters, PresentMon FPS and (optionally) LHM.

            Common: [--index N] [--interface <substring>] [--transport auto|hid|winusb]
                    [--report-id N] [--pipeline N]
            Exit codes: 0 ok, 2 not found, 3 busy (TRCC), 4 access denied,
                        5 handshake failed, 6 write failed.
            """);
        return ProbeExit.Ok;
    }
}
