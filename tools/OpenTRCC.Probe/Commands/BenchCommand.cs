using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenTRCC.Core.Encoding;
using OpenTRCC.Core.Imaging;
using OpenTRCC.Core.Rendering;
using OpenTRCC.Devices;
using OpenTRCC.Devices.Protocol;
using SkiaSharp;

namespace OpenTRCC.Probe.Commands;

/// <summary>
/// Sustained-write benchmark. Answers the M1 question: does the firmware accept
/// transfers larger than 512 bytes, and what frame rate is reachable?
/// </summary>
internal static class BenchCommand
{
    public static int Run(CommandLine line)
    {
        bool all = line.Has("all");
        var configs = new List<(int ChunkSize, int SettleMs)>();

        if (all)
        {
            foreach (int chunk in new[] { 512, 1024, 4096, 0 })
            {
                foreach (int settle in new[] { 0, 1 })
                {
                    configs.Add((chunk, settle));
                }
            }
        }
        else
        {
            configs.Add((ParseChunkSize(line.Get("chunk-size") ?? "512"), line.GetInt("settle-ms", 0)));
        }

        double seconds = line.GetDouble("seconds", all ? 5 : 10);

        using DeviceSession session = DeviceSession.Open(line);
        Console.WriteLine($"device:    {session.Interface.Path}");
        Console.WriteLine($"transport: {session.Kind}" +
                          (session.Transport is HidTransport hid ? $" (pipeline {hid.PipelineDepth})" : string.Empty));
        Console.WriteLine($"samples: {seconds:F1}s per configuration; the panel will freeze on the test image");

        HandshakeResult? handshake = null;
        try
        {
            handshake = session.Device.Handshake(HandshakeMode.Auto);
            Console.WriteLine($"handshake: {handshake.Mode} PM={handshake.ProductId} SUB={handshake.SubVersion}");
        }
        catch (LcdDeviceException ex)
        {
            Console.WriteLine($"warning: handshake failed ({ex.Message}); benchmarking with defaults");
        }

        DeviceProfile profile = handshake?.ProductId is int productId
            ? DeviceCatalog.ResolveOrDefault(productId, handshake.SubVersion ?? -1)
            : new DeviceProfile { ProductId = -1, SubVersion = -1 };

        byte[] payload = BuildPayload(profile);
        byte[] frame = FrameAssembler.Assemble(
            payload, profile.LineWidth, profile.LineHeight, profile.Encoding, 512);

        Console.WriteLine($"frame: payload {payload.Length} B, assembled {frame.Length} B, " +
                          $"{profile.Encoding} {profile.LineWidth}x{profile.LineHeight}");

        Evidence evidence = Evidence.Create(line);
        var results = new List<BenchResult>();

        foreach ((int chunkSize, int settleMs) in configs)
        {
            Console.Write($"chunk {DescribeChunk(chunkSize),6} settle {settleMs} ms ... ");
            try
            {
                BenchResult result = Measure(session, frame, chunkSize, settleMs, seconds);
                results.Add(result);
                Console.WriteLine($"{result.Status}: {result.Fps:F1} fps, {result.Message}");
            }
            catch (ShortWriteException ex)
            {
                results.Add(BenchResult.Failed(chunkSize, settleMs, "rejected", ex.Message));
                Console.WriteLine($"rejected: {ex.Message}");
                // A rejected chunk size may leave the firmware wedged; larger sizes are hopeless.
                if (chunkSize > 512)
                {
                    Console.WriteLine("stopping: the firmware does not accept this transfer size");
                    break;
                }
            }
            catch (LcdTimeoutException ex)
            {
                results.Add(BenchResult.Failed(chunkSize, settleMs, "timeout", ex.Message));
                Console.WriteLine($"timeout: {ex.Message}");
                break;
            }
            catch (LcdDeviceException ex)
            {
                results.Add(BenchResult.Failed(chunkSize, settleMs, "error", ex.Message));
                Console.WriteLine($"error: {ex.Message}");
                break;
            }
        }

        Console.WriteLine();
        PrintTable(results);

        string csv = BuildCsv(results);
        evidence.WriteText("bench.csv", csv);
        evidence.WriteText(
            "bench.md",
            $"# OpenTRCC probe bench\n\n- device: `{session.Interface.Path}`\n" +
            $"- pm: {profile.ProductId}, sub: {profile.SubVersion}\n" +
            $"- frame: {frame.Length} B ({payload.Length} B payload)\n\n" + MarkdownTable(results));

        Console.WriteLine($"saved: {evidence.PathFor("bench.csv")}");
        return results.Any(r => r.Status == "ok") ? ProbeExit.Ok : ProbeExit.WriteFailed;
    }

    private static byte[] BuildPayload(DeviceProfile profile)
    {
        using SKBitmap bitmap = TestPatternRenderer.Render(new TestPatternOptions
        {
            Width = profile.CanvasWidth,
            Height = profile.CanvasHeight,
            ProductId = profile.ProductId,
            SubVersion = profile.SubVersion,
            Mode = profile.Encoding == FrameEncoding.Jpeg ? "jpeg" : "rgb565",
            Rotation = profile.LineRotation,
            ByteOrder = profile.ByteOrder == Rgb565ByteOrder.BigEndian ? "be" : "le",
        });

        BgraImage canvasImage = TestPatternRenderer.ToBgra(bitmap);
        BgraImage lineImage = ImageTransforms.RotateClockwise(canvasImage, profile.LineRotation);

        if (profile.Encoding == FrameEncoding.Jpeg)
        {
            using SKBitmap rotated = TestPatternRenderer.FromBgra(lineImage);
            return TestPatternRenderer.EncodeJpeg(rotated, 85);
        }

        return Rgb565Encoder.Encode(lineImage, profile.ByteOrder);
    }

    private static BenchResult Measure(
        DeviceSession session, byte[] frame, int chunkSize, int settleMs, double seconds)
    {
        for (int i = 0; i < 3; i++)
        {
            session.Device.SendFrame(frame, chunkSize, settleMs);
        }

        var times = new List<double>(1024);
        var total = Stopwatch.StartNew();

        while (total.Elapsed.TotalSeconds < seconds)
        {
            var stopwatch = Stopwatch.StartNew();
            session.Device.SendFrame(frame, chunkSize, settleMs);
            stopwatch.Stop();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        total.Stop();

        double fps = times.Count / total.Elapsed.TotalSeconds;
        double p50 = Stats.Percentile(times, 50);
        double p95 = Stats.Percentile(times, 95);
        double bytesPerSecond = fps * frame.Length;

        return new BenchResult(
            chunkSize,
            settleMs,
            times.Count,
            Math.Round(fps, 2),
            Math.Round(bytesPerSecond, 0),
            Math.Round(p50, 2),
            Math.Round(p95, 2),
            "ok",
            $"{bytesPerSecond / 1024 / 1024:F2} MiB/s");
    }

    private static void PrintTable(IReadOnlyList<BenchResult> results)
    {
        Console.WriteLine("chunk     settle   frames     fps      B/s        p50 ms  p95 ms  status");
        foreach (BenchResult result in results)
        {
            Console.WriteLine(
                $"{DescribeChunk(result.ChunkSize),-9} {result.SettleMs,5} ms {result.Frames,7} {result.Fps,8:F1} " +
                $"{result.BytesPerSecond,10:F0} {result.P50Ms,8:F2} {result.P95Ms,7:F2}  {result.Status}");
        }
    }

    private static string BuildCsv(IReadOnlyList<BenchResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("chunk_size,settle_ms,frames,fps,bytes_per_second,p50_ms,p95_ms,status,message");
        foreach (BenchResult result in results)
        {
            builder.AppendLine(string.Join(',',
                result.ChunkSize.ToString(CultureInfo.InvariantCulture),
                result.SettleMs.ToString(CultureInfo.InvariantCulture),
                result.Frames.ToString(CultureInfo.InvariantCulture),
                result.Fps.ToString(CultureInfo.InvariantCulture),
                result.BytesPerSecond.ToString(CultureInfo.InvariantCulture),
                result.P50Ms.ToString(CultureInfo.InvariantCulture),
                result.P95Ms.ToString(CultureInfo.InvariantCulture),
                result.Status,
                $"\"{result.Message.Replace("\"", "\"\"")}\""));
        }

        return builder.ToString();
    }

    private static string MarkdownTable(IReadOnlyList<BenchResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("| chunk | settle | frames | fps | B/s | p50 ms | p95 ms | status |");
        builder.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (BenchResult result in results)
        {
            builder.AppendLine(
                $"| {DescribeChunk(result.ChunkSize)} | {result.SettleMs} ms | {result.Frames} | " +
                $"{result.Fps:F1} | {result.BytesPerSecond:F0} | {result.P50Ms:F2} | {result.P95Ms:F2} | {result.Status} |");
        }

        return builder.ToString();
    }

    private static string DescribeChunk(int chunkSize) => chunkSize <= 0 ? "full" : $"{chunkSize} B";

    private static int ParseChunkSize(string text) => text.ToLowerInvariant() switch
    {
        "full" or "0" => 0,
        _ => int.TryParse(text, out int value) && value > 0
            ? value
            : throw new ArgumentException("--chunk-size must be a positive byte count or 'full'."),
    };

    private sealed record BenchResult(
        int ChunkSize,
        int SettleMs,
        int Frames,
        double Fps,
        double BytesPerSecond,
        double P50Ms,
        double P95Ms,
        string Status,
        string Message)
    {
        public static BenchResult Failed(int chunkSize, int settleMs, string status, string message)
            => new(chunkSize, settleMs, 0, 0, 0, 0, 0, status, message);
    }
}
