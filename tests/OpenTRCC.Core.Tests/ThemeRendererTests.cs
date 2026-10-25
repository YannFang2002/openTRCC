using OpenTRCC.Core.Data;
using OpenTRCC.Core.Rendering;
using OpenTRCC.Core.Themes;
using SkiaSharp;
using Xunit;

namespace OpenTRCC.Core.Tests;

public sealed class ThemeRendererTests
{
    private static SensorSnapshot Snapshot() => SnapshotAt(
        DateTimeOffset.Parse("2026-10-24T21:30:00+08:00"), 80);

    private static SensorSnapshot SnapshotAt(DateTimeOffset at, double fps) => new(
        at,
        new Dictionary<string, SensorValue>
        {
            ["cpu.temp.package"] = SensorValue.FromNumber(62),
            ["gpu.load.core"] = SensorValue.FromNumber(50),
            ["fps.current"] = SensorValue.FromNumber(fps),
        });

    [Fact]
    public void Draws_Solid_Background_And_Two_Tone_Bar()
    {
        var theme = new ThemeDocument
        {
            Canvas = new ThemeCanvas
            {
                Width = 64,
                Height = 48,
                Background = new ThemeBackground { Kind = BackgroundKind.Solid, Colors = new[] { "#102030" } },
            },
            Layers = new Layer[]
            {
                new BarLayer
                {
                    Id = "bar", X = 0, Y = 0, W = 64, H = 8,
                    Value = "{gpu.load.core}", Min = 0, Max = 100,
                    Track = "#0000FF", Fill = "#FF0000", Radius = 0,
                },
            },
        };

        using SKBitmap bitmap = new ThemeRenderer().Render(theme, Snapshot(), TimeSpan.Zero);

        Assert.Equal(64, bitmap.Width);
        Assert.Equal(48, bitmap.Height);

        SKColor background = bitmap.GetPixel(60, 40);
        Assert.Equal(0x10, background.Red);
        Assert.Equal(0x20, background.Green);
        Assert.Equal(0x30, background.Blue);

        SKColor fill = bitmap.GetPixel(10, 4);
        Assert.True(fill.Red > 200, $"expected red fill, got {fill}");

        SKColor track = bitmap.GetPixel(60, 4);
        Assert.True(track.Blue > 200, $"expected blue track, got {track}");
    }

    [Fact]
    public void Bar_Fill_Follows_Sensor_Value()
    {
        static ThemeDocument Theme(int value) => new()
        {
            Canvas = new ThemeCanvas { Width = 100, Height = 10 },
            Layers = new Layer[]
            {
                new BarLayer
                {
                    X = 0, Y = 0, W = 100, H = 10, Value = value.ToString(),
                    Min = 0, Max = 100, Track = "#000000", Fill = "#00FF00", Radius = 0,
                },
            },
        };

        using SKBitmap half = new ThemeRenderer().Render(Theme(50), Snapshot(), TimeSpan.Zero);
        using SKBitmap tenth = new ThemeRenderer().Render(Theme(10), Snapshot(), TimeSpan.Zero);

        Assert.True(half.GetPixel(40, 5).Green > 200);
        Assert.True(half.GetPixel(60, 5).Green < 50);
        Assert.True(tenth.GetPixel(5, 5).Green > 200);
        Assert.True(tenth.GetPixel(20, 5).Green < 50);
    }

    [Fact]
    public void Draws_Ring_And_Text_Without_Throwing()
    {
        var theme = new ThemeDocument
        {
            Canvas = new ThemeCanvas { Width = 160, Height = 160 },
            Layers = new Layer[]
            {
                new RingLayer
                {
                    X = 10, Y = 10, W = 140, H = 140,
                    Value = "{cpu.temp.package}", Min = 20, Max = 100,
                    Thickness = 12, Track = "#222222", Fill = new[] { "#5EEAD4" },
                },
                new TextLayer
                {
                    X = 10, Y = 60, W = 140, H = 40,
                    Text = "{cpu.temp.package:0}°", Font = new FontSpec { Size = 30 },
                    Color = "#FFFFFF", Align = "center", VerticalAlign = "middle",
                },
            },
        };

        using SKBitmap bitmap = new ThemeRenderer().Render(theme, Snapshot(), TimeSpan.Zero);

        // Ring arc at 12 o'clock should not be the default black background.
        SKColor arc = bitmap.GetPixel(80, 16);
        Assert.True(arc.Red + arc.Green + arc.Blue > 0);

        int lit = 0;
        for (int y = 60; y < 100; y++)
        {
            for (int x = 40; x < 120; x++)
            {
                if (bitmap.GetPixel(x, y).Red > 100)
                {
                    lit++;
                }
            }
        }

        Assert.True(lit > 20, "text layer drew nothing");
    }

    [Fact]
    public void Sparkline_Tracks_History_Across_Frames()
    {
        var theme = new ThemeDocument
        {
            Canvas = new ThemeCanvas { Width = 100, Height = 50 },
            Layers = new Layer[]
            {
                new SparklineLayer
                {
                    X = 0, Y = 0, W = 100, H = 50, Source = "{fps.current}",
                    Samples = 10, Min = 0, Max = 100, Stroke = "#FF0000", LineWidth = 2,
                },
            },
        };

        var renderer = new ThemeRenderer();
        int lit = 0;

        DateTimeOffset baseline = DateTimeOffset.Parse("2026-10-24T21:30:00+08:00");
        for (int i = 0; i < 12; i++)
        {
            SensorSnapshot snapshot = SnapshotAt(baseline.AddSeconds(i), 80 + i);
            using SKBitmap bitmap = renderer.Render(theme, snapshot, TimeSpan.FromMilliseconds(i * 40));
            lit = 0;
            for (int x = 0; x < 100; x++)
            {
                for (int y = 0; y < 50; y++)
                {
                    if (bitmap.GetPixel(x, y).Red > 100)
                    {
                        lit++;
                    }
                }
            }
        }

        Assert.True(lit > 20, "sparkline never drew a line");
    }

    [Fact]
    public void Same_Snapshot_Renders_Identical_Pixels()
    {
        var theme = new ThemeDocument
        {
            Canvas = new ThemeCanvas { Width = 100, Height = 50 },
            Layers = new Layer[]
            {
                new SparklineLayer
                {
                    X = 0, Y = 0, W = 100, H = 50, Source = "{fps.current}",
                    Samples = 10, Min = 0, Max = 100, Stroke = "#FF0000", LineWidth = 2,
                },
            },
        };

        var renderer = new ThemeRenderer();
        SensorSnapshot snapshot = SnapshotAt(DateTimeOffset.Parse("2026-10-24T21:30:00+08:00"), 80);

        byte[] first;
        using (SKBitmap a = renderer.Render(theme, snapshot, TimeSpan.Zero))
        {
            first = a.Bytes ?? Array.Empty<byte>();
        }

        byte[] second;
        using (SKBitmap b = renderer.Render(theme, snapshot, TimeSpan.Zero))
        {
            second = b.Bytes ?? Array.Empty<byte>();
        }

        // Same data must hash to the same frame; otherwise FramePump resends every render.
        Assert.Equal(first, second);
    }

    [Fact]
    public void Missing_Image_Asset_Is_Not_Fatal()
    {
        var theme = new ThemeDocument
        {
            Canvas = new ThemeCanvas { Width = 32, Height = 32 },
            Layers = new Layer[] { new ImageLayer { X = 0, Y = 0, W = 32, H = 32, Asset = "nope.png" } },
        };

        using SKBitmap bitmap = new ThemeRenderer().Render(theme, Snapshot(), TimeSpan.Zero);

        Assert.Equal(32, bitmap.Width);
    }

    [Fact]
    public void HideWhenMissing_Draws_Nothing_For_Unknown_Sensor()
    {
        var theme = new ThemeDocument
        {
            Canvas = new ThemeCanvas
            {
                Width = 120,
                Height = 60,
                Background = new ThemeBackground { Kind = BackgroundKind.Solid, Colors = new[] { "#000000" } },
            },
            Layers = new Layer[]
            {
                new TextLayer
                {
                    X = 0, Y = 0, W = 120, H = 60,
                    Text = "{no.such.sensor:0}",
                    Font = new FontSpec { Size = 40 },
                    Color = "#FFFFFF",
                    Align = "center",
                    VerticalAlign = "middle",
                    HideWhenMissing = true,
                },
            },
        };

        using SKBitmap bitmap = new ThemeRenderer().Render(theme, Snapshot(), TimeSpan.Zero);

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Assert.Equal(0, bitmap.GetPixel(x, y).Red);
            }
        }
    }
}
