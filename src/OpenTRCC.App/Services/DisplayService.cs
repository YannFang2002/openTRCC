using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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

namespace OpenTRCC.App.Services;

public sealed record PreviewFrame(byte[] Bgra, int Width, int Height);

/// <summary>
/// Owns the device handle, the sensor aggregator and the render/send loop.
/// All device work happens on one background thread; the UI only receives
/// preview bitmaps and status strings.
/// </summary>
public sealed class DisplayService : IDisposable
{
    private readonly object _gate = new();
    private readonly string _themeDirectory;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private SensorAggregator? _aggregator;
    private ThemeDocument? _theme;
    private volatile bool _running;

    public DisplayService(string themeDirectory)
    {
        _themeDirectory = themeDirectory;
    }

    public event Action<string>? StatusChanged;

    public event Action<PreviewFrame>? PreviewReady;

    public event Action<SensorSnapshot>? SensorsUpdated;

    public bool IsRunning => _running;

    public string? DeviceSummary { get; private set; }

    /// <summary>Descriptor list from the running aggregator (empty before the first poll).</summary>
    public IReadOnlyList<SensorDescriptor> Descriptors =>
        _aggregator?.Descriptors ?? Array.Empty<SensorDescriptor>();

    /// <summary>Sources of the running aggregator, for the settings page.</summary>
    public IReadOnlyList<ISensorSource> ActiveSources =>
        _aggregator?.Sources ?? Array.Empty<ISensorSource>();

    /// <summary>Latest snapshot (empty before the first poll).</summary>
    public SensorSnapshot CurrentSnapshot => _aggregator?.Current ?? SensorSnapshot.Empty;

    public string? ThemeId => _theme?.Id;

    public void Start(ThemeDocument theme)
    {
        lock (_gate)
        {
            if (_running)
            {
                _theme = theme;
                return;
            }

            _theme = theme;
            _cts = new CancellationTokenSource();
            _running = true;
            _thread = new Thread(() => Run(_cts.Token))
            {
                IsBackground = true,
                Name = "OpenTRCC.Display",
            };
            _thread.Start();
        }
    }

    public void UpdateTheme(ThemeDocument theme) => _theme = theme;

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _cts?.Cancel();
            _thread?.Join(TimeSpan.FromSeconds(5));
            _running = false;
            _cts?.Dispose();
            _cts = null;
            _thread = null;
        }
    }

    public void Dispose() => Stop();

    private void Run(CancellationToken token)
    {
        ThermalrightType2? device = null;
        DeviceProfile? profile = null;
        IDisposable? backgroundDisposable = null;

        try
        {
            Status("正在查找屏幕…");
            IReadOnlyList<LcdInterface> targets = DeviceEnumerator.FindHidTargets();
            if (targets.Count == 0)
            {
                Status("未找到 0416:5302 屏幕，请检查 9Pin 线与 WinUSB 驱动");
                return;
            }

            Status("正在连接屏幕…");
            var transport = HidTransport.Open(targets[0].Path, reportId: 0, pipelineDepth: 2);
            device = new ThermalrightType2(transport) { SettleMs = 0 };
            HandshakeResult handshake = device.Handshake(HandshakeMode.Auto);
            profile = DeviceCatalog.ResolveOrDefault(
                handshake.ProductId ?? -1, handshake.SubVersion ?? -1);

            DeviceSummary = $"PM={profile.ProductId} SUB={profile.SubVersion} · " +
                            $"{profile.LineWidth}x{profile.LineHeight} · {profile.ByteOrder} · rot{profile.LineRotation}";

            _aggregator = new SensorAggregator(new ISensorSource[]
            {
                new SystemSensorSource(),
                new LhmSensorSource(),
                new PresentMonFpsSource(),
            });
            var seenErrors = new HashSet<string>(StringComparer.Ordinal);

            _aggregator.Updated += snapshot =>
            {
                foreach (string error in _aggregator.LastErrors)
                {
                    if (seenErrors.Add(error))
                    {
                        Log.Write($"sensor error: {error}");
                    }
                }

                SensorsUpdated?.Invoke(snapshot);
            };

            _aggregator.Start(TimeSpan.FromSeconds(1));

            foreach (ISensorSource source in _aggregator.Sources)
            {
                Log.Write($"source {source.Id}: available={source.IsAvailable}" +
                          (source.IsAvailable ? string.Empty : $" ({source.UnavailableReason})"));
            }

            var renderer = new ThemeRenderer { BaseDirectory = _themeDirectory };
            var pump = new FramePump(TimeSpan.FromSeconds(1.2));
            var clock = Stopwatch.StartNew();
            TimeSpan lastPreview = TimeSpan.Zero;
            TimeSpan lastStatus = TimeSpan.Zero;
            int sent = 0;
            int rendered = 0;
            string? loadedThemeId = null;
            IBackgroundFrameProvider? backgroundFrames = null;
            string? loadedBackgroundAsset = null;

            Status("已连接，正在推流");

            while (!token.IsCancellationRequested)
            {
                var frameStart = clock.Elapsed;
                ThemeDocument theme = _theme ?? new ThemeDocument();
                SensorSnapshot snapshot = _aggregator.Current;

                // GIF background: reload only when the theme or its asset changes.
                string? asset = theme.Canvas.Background.Asset;
                if (loadedThemeId != theme.Id || loadedBackgroundAsset != asset)
                {
                    backgroundDisposable?.Dispose();
                    backgroundFrames = BackgroundMedia.CreateFor(theme, _themeDirectory);
                    backgroundDisposable = backgroundFrames as IDisposable;
                    renderer.BackgroundFrames = backgroundFrames;
                    loadedThemeId = theme.Id;
                    loadedBackgroundAsset = asset;
                }

                using SKBitmap bitmap = renderer.Render(theme, snapshot, clock.Elapsed);
                BgraImage canvas = TestPatternRenderer.ToBgra(bitmap);

                if (clock.Elapsed - lastPreview >= TimeSpan.FromMilliseconds(100))
                {
                    PreviewReady?.Invoke(new PreviewFrame(canvas.Pixels, canvas.Width, canvas.Height));
                    lastPreview = clock.Elapsed;
                }

                BgraImage oriented = ImageTransforms.RotateAndFlip(canvas, profile.LineRotation, profile.Flip);
                byte[] payload = payloadFor(profile, oriented, bitmap);
                byte[] packet = FrameAssembler.Assemble(
                    payload, oriented.Width, oriented.Height, profile.Encoding, 512);

                if (pump.ShouldSend(packet, DateTimeOffset.Now))
                {
                    device.SendFrame(packet, 512, settleMs: 0);
                    sent++;
                }

                rendered++;

                if (clock.Elapsed - lastStatus >= TimeSpan.FromSeconds(1))
                {
                    SensorSnapshot latest = _aggregator.Current;
                    Status($"运行中 · 渲染 {rendered} / 发送 {sent} · " +
                           $"FPS {Value(latest, "fps.current", "0")} · " +
                           $"CPU {Value(latest, "cpu.load.total", "0")}% · " +
                           $"GPU {Value(latest, "gpu.temp.core", "0")}°");
                    lastStatus = clock.Elapsed;
                }

                int elapsedMs = (int)(clock.Elapsed - frameStart).TotalMilliseconds;
                int delay = 50 - elapsedMs;
                if (delay > 0)
                {
                    token.WaitHandle.WaitOne(delay);
                }
            }
        }
        catch (DeviceBusyException)
        {
            Status("设备被 TRCC 占用，请关闭 TRCC 后点击“开始”");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Status($"错误：{ex.Message}");
        }
        finally
        {
            device?.Dispose();
            backgroundDisposable?.Dispose();
            _aggregator?.Dispose();
            _aggregator = null;
            _running = false;
            DeviceSummary = null;
        }
    }

    private static string Value(SensorSnapshot snapshot, string id, string format)
    {
        SensorValue value = snapshot.Find(id);
        return value.IsMissing ? "—" : value.Display(format);
    }

    private static byte[] payloadFor(DeviceProfile profile, BgraImage oriented, SKBitmap bitmap)
    {
        if (profile.Encoding == FrameEncoding.Jpeg)
        {
            using SKBitmap rotated = TestPatternRenderer.FromBgra(oriented);
            return TestPatternRenderer.EncodeJpeg(rotated, 85);
        }

        return Rgb565Encoder.Encode(oriented, profile.ByteOrder);
    }

    private void Status(string message)
    {
        Log.Write($"status: {message}");
        StatusChanged?.Invoke(message);
    }
}
