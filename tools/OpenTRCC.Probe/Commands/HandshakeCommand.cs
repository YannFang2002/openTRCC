using System.Text;
using OpenTRCC.Core.Diagnostics;
using OpenTRCC.Devices;
using OpenTRCC.Devices.Protocol;

namespace OpenTRCC.Probe.Commands;

internal static class HandshakeCommand
{
    public static int Run(CommandLine line)
    {
        HandshakeMode mode = ParseMode(line.Get("handshake") ?? line.Get("mode") ?? "auto");

        using DeviceSession session = DeviceSession.Open(line);
        Console.WriteLine($"device:    {session.Interface.Path}");
        Console.WriteLine($"transport: {session.Kind}");
        Console.WriteLine($"mode:      {mode}");

        HandshakeResult result;
        try
        {
            result = session.Device.Handshake(mode);
        }
        catch (LcdDeviceException ex)
        {
            Console.Error.WriteLine($"handshake failed: {ex.Message}");
            return ProbeExit.HandshakeFailed;
        }

        Console.WriteLine($"result: {result.Mode}");
        if (result.IsStandard)
        {
            Console.WriteLine($"PM:     {result.ProductId}");
            Console.WriteLine($"SUB:    {result.SubVersion}");
            Console.WriteLine($"serial: {result.SerialNumber ?? "<none>"}");

            if (DeviceCatalog.TryResolve(result.ProductId ?? -1, out DeviceProfile profile))
            {
                Console.WriteLine($"catalog: {profile.LineWidth}x{profile.LineHeight} {profile.Encoding} " +
                                  $"{profile.ByteOrder} rot{profile.LineRotation}");
            }
            else
            {
                Console.WriteLine("catalog: unknown PM - pick the resolution manually");
            }
        }

        Console.WriteLine();
        Console.WriteLine(Handshake.Annotate(result.Response));
        Console.WriteLine(HexDump.Format(result.Response, 16));

        Evidence evidence = Evidence.Create(line);
        var capture = new StringBuilder();
        capture.AppendLine("# OpenTRCC handshake capture");
        capture.AppendLine($"# time: {DateTimeOffset.Now:O}");
        capture.AppendLine($"# interface: {session.Interface.Path}");
        capture.AppendLine($"# instance: {session.Interface.InstanceId}");
        capture.AppendLine($"# mode: {result.Mode}");
        capture.AppendLine();
        capture.AppendLine("# request (512 bytes)");
        capture.Append(HexDump.Format(Handshake.BuildRequest()));
        capture.AppendLine();
        capture.AppendLine($"# response ({result.Response.Length} bytes)");
        capture.Append(HexDump.Format(result.Response));
        capture.AppendLine();
        capture.AppendLine("# annotation");
        capture.Append(Handshake.Annotate(result.Response));
        evidence.WriteText("handshake.hex", capture.ToString());

        Console.WriteLine($"saved: {evidence.PathFor("handshake.hex")}");
        return ProbeExit.Ok;
    }

    private static HandshakeMode ParseMode(string text) => text.ToLowerInvariant() switch
    {
        "auto" => HandshakeMode.Auto,
        "standard" => HandshakeMode.Standard,
        "streamed" => HandshakeMode.Streamed,
        _ => throw new ArgumentException($"Unknown handshake mode '{text}' (auto|standard|streamed)."),
    };
}
