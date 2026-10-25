using System.Diagnostics;
using OpenTRCC.Core.Data;
using OpenTRCC.Core.Encoding;
using OpenTRCC.Core.Imaging;
using OpenTRCC.Core.Rendering;
using OpenTRCC.Core.Themes;
using OpenTRCC.Devices;
using OpenTRCC.Devices.Protocol;
using OpenTRCC.Sensors;
using OpenTRCC.Sensors.PresentMon;
using SkiaSharp;

namespace OpenTRCC.Probe.Commands;

/// <summary>Renders a theme JSON to PNG and (optionally) streams it to the panel.</summary>
internal static class RenderCommand
{
    public static int Run(CommandLine line)
    {
        string themePath = line.Get("theme") ?? Path.Combine("themes", "pulse-320x240.json");
        if (!File.Exists(themePath))
        {
            Console.Error.WriteLine($"theme not found: {themePath}");
            return ProbeExit.Usage;
        }

        ThemeDocument theme = ThemeJson.Load(themePath);
        var renderer = new ThemeRenderer
        {
            BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(themePath)),
        };

        SensorSnapshot snapshot = SensorSnapshot.Sample();
        SensorAggregator? aggregator = null;
        bool live = line.Has("live");

        if (live)
        {
            var sources = new List<ISensorSource>
            {
                new SystemSensorSource(),
                new LhmSensorSource(),
                new PresentMonFpsSource(),
            };

            aggregator = new SensorAggregator(sources);
            aggregator.Start(TimeSpan.FromSeconds(1));

            // Two polls: the first primes delta-based counters (CPU, network) and
            // gives LHM/PresentMon a moment to enumerate their sensors.
            aggregator.PollOnce();
            Thread.Sleep(400);
            snapshot = aggregator.PollOnce();

            foreach (ISensorSource source in sources)
            {
                Console.WriteLine($"source:   {source.Id,-10} available={source.IsAvailable}" +
                                  (source.IsAvailable ? string.Empty : $" ({source.UnavailableReason})"));
            }
        }

        Evidence evidence = Evidence.Create(line);

        var stopwatch = Stopwatch.StartNew();
        using SKBitmap bitmap = renderer.Render(theme, snapshot, TimeSpan.Zero);
        stopwatch.Stop();

        string pngPath = line.Get("png") ?? evidence.PathFor($"{theme.Id}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pngPath))!);
        File.WriteAllBytes(pngPath, TestPatternRenderer.EncodePng(bitmap));

        Console.WriteLine($"theme:    {theme.Id} \"{theme.Name}\" ({theme.Layers.Count} layers)");
        Console.WriteLine($"canvas:   {theme.Canvas.Width}x{theme.Canvas.Height}, background {theme.Canvas.Background.Kind}");
        Console.WriteLine($"render:   {stopwatch.Elapsed.TotalMilliseconds:F2} ms" + (live ? " (live data)" : " (sample data)"));
        Console.WriteLine($"png:      {Path.GetFullPath(pngPath)}");

        if (!line.Has("push"))
        {
            aggregator?.Dispose();
            return ProbeExit.Ok;
        }

        try
        {
            return Push(line, theme, renderer, () => aggregator?.Current ?? snapshot);
        }
        finally
        {
            aggregator?.Dispose();
        }
    }

    private static int Push(
        CommandLine line, ThemeDocument theme, ThemeRenderer renderer, Func<SensorSnapshot> snapshotProvider)
    {
        double seconds = line.GetDouble("seconds", 10);

        using DeviceSession session = DeviceSession.Open(line);
        HandshakeResult handshake = session.Device.Handshake(HandshakeMode.Auto);
        DeviceProfile profile = handshake.ProductId is int productId
            ? DeviceCatalog.ResolveOrDefault(productId, handshake.SubVersion ?? -1)
            : new DeviceProfile();

        Console.WriteLine($"device:   {session.Kind} PM={profile.ProductId} " +
                          $"{profile.LineWidth}x{profile.LineHeight} rot{profile.LineRotation} {profile.ByteOrder}");

        var frameTimes = new List<double>(1024);
        int frames = 0;
        var total = Stopwatch.StartNew();

        var pump = new FramePump(TimeSpan.FromSeconds(line.GetDouble("keep-alive", 1.2)));
        bool throttle = !line.Has("every-frame");

        while (total.Elapsed.TotalSeconds < seconds)
        {
            using SKBitmap frame = renderer.Render(theme, snapshotProvider(), total.Elapsed);
            BgraImage canvas = TestPatternRenderer.ToBgra(frame);
            BgraImage oriented = ImageTransforms.RotateAndFlip(canvas, profile.LineRotation, profile.Flip);
            byte[] payload = Rgb565Encoder.Encode(oriented, profile.ByteOrder);
            byte[] packet = FrameAssembler.Assemble(
                payload, oriented.Width, oriented.Height, FrameEncoding.Rgb565, 512);

            // Static themes must not burn 300 reports per frame; keep-alive keeps
            // the panel out of its boot logo without resending identical frames.
            if (throttle && !pump.ShouldSend(packet, DateTimeOffset.Now))
            {
                Thread.Sleep(50);
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            session.Device.SendFrame(packet, profile.ChunkSize <= 0 ? 512 : profile.ChunkSize, profile.SettleMs);
            stopwatch.Stop();

            frameTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            frames++;
        }

        total.Stop();
        double fps = frames / total.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine($"pushed:   {frames} frames in {total.Elapsed.TotalSeconds:F1}s -> {fps:F1} fps " +
                          $"(p50 {Stats.Percentile(frameTimes, 50):F1} ms)" +
                          (throttle ? $", skipped {pump.Skipped} unchanged" : string.Empty));
        return ProbeExit.Ok;
    }
}
