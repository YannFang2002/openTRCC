using System.Diagnostics;
using System.Text.Json;
using OpenTRCC.Core.Encoding;
using OpenTRCC.Core.Imaging;
using OpenTRCC.Core.Rendering;
using OpenTRCC.Devices;
using OpenTRCC.Devices.Protocol;
using SkiaSharp;

namespace OpenTRCC.Probe.Commands;

/// <summary>Streams the diagnostic test image with a moving bar so orientation, colour and refresh are visible.</summary>
internal static class PatternCommand
{
    public static int Run(CommandLine line)
    {
        string modeText = (line.Get("mode") ?? "auto").ToLowerInvariant();
        if (modeText is not ("auto" or "rgb565" or "jpeg"))
        {
            throw new ArgumentException("--mode must be auto, rgb565 or jpeg.");
        }

        FlipMode flip = ParseFlip(line.Get("flip") ?? "none");
        int chunkSize = line.GetInt("chunk-size", 512);
        int settleMs = line.GetInt("settle-ms", 1);
        double seconds = line.GetDouble("seconds", 10);

        using DeviceSession session = DeviceSession.Open(line);
        Console.WriteLine($"device:    {session.Interface.Path}");
        Console.WriteLine($"transport: {session.Kind}" +
                          (session.Transport is HidTransport hid ? $" (pipeline {hid.PipelineDepth})" : string.Empty));

        HandshakeResult? handshake = null;
        try
        {
            HandshakeMode mode = ParseHandshakeMode(line.Get("handshake") ?? "auto");
            handshake = session.Device.Handshake(mode);
            Console.WriteLine(handshake.IsStandard
                ? $"handshake: standard PM={handshake.ProductId} SUB={handshake.SubVersion} serial={handshake.SerialNumber ?? "<none>"}"
                : $"handshake: streamed PM={handshake.ProductId} SUB={handshake.SubVersion} ({handshake.Response.Length} bytes)");
        }
        catch (LcdDeviceException ex)
        {
            Console.WriteLine($"warning: handshake failed ({ex.Message}); continuing with default profile");
        }

        DeviceProfile profile = handshake?.ProductId is int productId
            ? DeviceCatalog.ResolveOrDefault(productId, handshake.SubVersion ?? -1)
            : new DeviceProfile { ProductId = -1, SubVersion = -1 };

        int rotate = line.Has("rotate") ? line.GetInt("rotate", profile.LineRotation) : profile.LineRotation;
        if (rotate is not (0 or 90 or 180 or 270))
        {
            throw new ArgumentException("--rotate must be 0, 90, 180 or 270.");
        }

        Rgb565ByteOrder byteOrder = line.Has("byte-order")
            ? ParseByteOrder(line.Get("byte-order")!)
            : profile.ByteOrder;

        FrameEncoding encoding = modeText switch
        {
            "jpeg" => FrameEncoding.Jpeg,
            "rgb565" => FrameEncoding.Rgb565,
            _ => profile.Encoding,
        };

        bool rotated = rotate is 90 or 270;
        int lineWidth = rotated ? 240 : 320;
        int lineHeight = rotated ? 320 : 240;

        Evidence evidence = Evidence.Create(line);
        var options = new TestPatternOptions
        {
            Width = 320,
            Height = 240,
            ProductId = profile.ProductId,
            SubVersion = profile.SubVersion,
            Mode = encoding == FrameEncoding.Jpeg ? "jpeg" : "rgb565",
            Rotation = rotate,
            ByteOrder = byteOrder == Rgb565ByteOrder.BigEndian ? "be" : "le",
            ChunkSize = chunkSize,
        };

        Console.WriteLine($"profile: PM={profile.ProductId} SUB={profile.SubVersion} " +
                          $"{lineWidth}x{lineHeight} {encoding} {byteOrder} rot{rotate} flip={flip} chunk={chunkSize}");

        var frameTimes = new List<double>(1024);
        int frames = 0;
        long frameBytes = 0;
        int chunkCount = 0;
        string pngName = $"pattern-{(encoding == FrameEncoding.Jpeg ? "jpeg" : "rgb565")}-rot{rotate}-" +
                         $"{(byteOrder == Rgb565ByteOrder.BigEndian ? "be" : "le")}.png";

        var total = Stopwatch.StartNew();
        while (total.Elapsed.TotalSeconds < seconds)
        {
            double phase = frames % 60 / 60.0;
            using SKBitmap bitmap = TestPatternRenderer.Render(options with { Phase = phase });

            if (frames == 0)
            {
                byte[] png = TestPatternRenderer.EncodePng(bitmap);
                evidence.WriteBytes(pngName, png);
                if (line.Has("save-png"))
                {
                    evidence.WriteBytes("pattern.png", png);
                }
            }

            BgraImage canvasImage = TestPatternRenderer.ToBgra(bitmap);
            BgraImage lineImage = ImageTransforms.RotateAndFlip(canvasImage, rotate, flip);

            byte[] payload;
            if (encoding == FrameEncoding.Jpeg)
            {
                using SKBitmap rotatedBitmap = TestPatternRenderer.FromBgra(lineImage);
                payload = TestPatternRenderer.EncodeJpeg(rotatedBitmap, 85);
            }
            else
            {
                payload = Rgb565Encoder.Encode(lineImage, byteOrder);
            }

            byte[] frame = FrameAssembler.Assemble(payload, lineImage.Width, lineImage.Height, encoding, 512);
            frameBytes = frame.Length;
            chunkCount = chunkSize <= 0 ? 1 : FrameChunker.Count(frame.Length, chunkSize);

            var stopwatch = Stopwatch.StartNew();
            session.Device.SendFrame(frame, chunkSize, settleMs);
            stopwatch.Stop();

            frameTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            frames++;
        }

        total.Stop();

        double fps = frames / total.Elapsed.TotalSeconds;
        double p50 = Stats.Percentile(frameTimes, 50);
        double p95 = Stats.Percentile(frameTimes, 95);

        Console.WriteLine();
        Console.WriteLine($"frames:   {frames} in {total.Elapsed.TotalSeconds:F2}s -> {fps:F1} fps");
        Console.WriteLine($"per frame: p50 {p50:F2} ms, p95 {p95:F2} ms (includes {settleMs} ms settle)");
        Console.WriteLine($"layout:   payload {frameBytes} B, {chunkCount} writes/frame of {(chunkSize <= 0 ? frameBytes : chunkSize)} B");
        Console.WriteLine($"png:      {evidence.PathFor(pngName)}");

        var summary = new
        {
            device = session.Interface.Path,
            pm = profile.ProductId,
            sub = profile.SubVersion,
            encoding = encoding.ToString(),
            byteOrder = byteOrder.ToString(),
            rotate,
            flip = flip.ToString(),
            chunkSize,
            settleMs,
            frames,
            fps = Math.Round(fps, 2),
            frameBytes,
            chunkCount,
            p50Ms = Math.Round(p50, 2),
            p95Ms = Math.Round(p95, 2),
        };
        evidence.WriteText(
            "pattern.json",
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        if (line.Has("save-profile"))
        {
            DeviceProfile confirmed = profile with
            {
                CanvasWidth = 320,
                CanvasHeight = 240,
                LineWidth = lineWidth,
                LineHeight = lineHeight,
                Encoding = encoding,
                ByteOrder = byteOrder,
                LineRotation = rotate,
                Flip = flip,
                ChunkSize = chunkSize <= 0 ? 512 : chunkSize,
                SettleMs = settleMs,
                ConfirmedAt = DateTimeOffset.Now,
                Notes = $"confirmed via probe pattern {encoding} rot{rotate} {byteOrder}",
            };

            var store = new DeviceProfileStore();
            store.Save(confirmed);
            Console.WriteLine($"profile saved: {store.FilePath}");
        }

        return ProbeExit.Ok;
    }

    private static Rgb565ByteOrder ParseByteOrder(string text) => text.ToLowerInvariant() switch
    {
        "le" or "little" or "littleendian" => Rgb565ByteOrder.LittleEndian,
        "be" or "big" or "bigendian" => Rgb565ByteOrder.BigEndian,
        _ => throw new ArgumentException("--byte-order must be le or be."),
    };

    private static HandshakeMode ParseHandshakeMode(string text) => text.ToLowerInvariant() switch
    {
        "auto" => HandshakeMode.Auto,
        "standard" => HandshakeMode.Standard,
        "streamed" => HandshakeMode.Streamed,
        _ => throw new ArgumentException("--handshake must be auto, standard or streamed."),
    };

    private static FlipMode ParseFlip(string text) => text.ToLowerInvariant() switch
    {
        "none" => FlipMode.None,
        "h" or "horizontal" => FlipMode.Horizontal,
        "v" or "vertical" => FlipMode.Vertical,
        "hv" or "both" => FlipMode.Both,
        _ => throw new ArgumentException("--flip must be none, h, v or hv."),
    };
}
