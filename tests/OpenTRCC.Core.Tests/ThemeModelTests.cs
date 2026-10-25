using OpenTRCC.Core.Binding;
using OpenTRCC.Core.Data;
using OpenTRCC.Core.Themes;
using Xunit;

namespace OpenTRCC.Core.Tests;

public sealed class ThemeModelTests
{
    private static ThemeDocument SampleTheme() => new()
    {
        Id = "test",
        Name = "Test",
        Canvas = new ThemeCanvas
        {
            Width = 320,
            Height = 240,
            Background = new ThemeBackground
            {
                Kind = BackgroundKind.Gradient,
                Colors = new[] { "#0B1020", "#1B2A4A" },
                Angle = 135,
            },
        },
        Layers = new Layer[]
        {
            new RingLayer { Id = "ring", X = 10, Y = 10, W = 100, H = 100, Value = "{cpu.temp.package}", Max = 100 },
            new TextLayer { Id = "text", X = 10, Y = 10, W = 100, H = 20, Text = "{cpu.temp.package:0}°" },
            new BarLayer { Id = "bar", X = 10, Y = 40, W = 100, H = 8, Value = "{gpu.load.core}" },
            new SparklineLayer { Id = "spark", X = 10, Y = 60, W = 100, H = 20, Source = "{fps.current}" },
            new ImageLayer { Id = "img", X = 0, Y = 0, W = 10, H = 10, Asset = "missing.png" },
        },
    };

    [Fact]
    public void Json_Roundtrip_Keeps_Every_Layer_Type()
    {
        ThemeDocument theme = SampleTheme();

        string json = ThemeJson.Serialize(theme);
        ThemeDocument back = ThemeJson.Deserialize(json);

        Assert.Equal(theme.Layers.Count, back.Layers.Count);
        Assert.IsType<RingLayer>(back.Layers[0]);
        Assert.IsType<TextLayer>(back.Layers[1]);
        Assert.IsType<BarLayer>(back.Layers[2]);
        Assert.IsType<SparklineLayer>(back.Layers[3]);
        Assert.IsType<ImageLayer>(back.Layers[4]);
        Assert.Equal(BackgroundKind.Gradient, back.Canvas.Background.Kind);
        Assert.Equal(320, back.Canvas.Width);
    }

    [Fact]
    public void Json_Contains_Discriminator_And_Enum_Names()
    {
        string json = ThemeJson.Serialize(SampleTheme());

        Assert.Contains("\"type\": \"ring\"", json);
        Assert.Contains("\"kind\": \"gradient\"", json);
    }

    [Fact]
    public void Json_Tolerates_Comments_And_Trailing_Commas()
    {
        const string json = """
            {
              // a comment
              "schemaVersion": 1,
              "id": "x",
              "canvas": { "width": 320, "height": 240 },
              "layers": [],
            }
            """;

        ThemeDocument theme = ThemeJson.Deserialize(json);

        Assert.Equal("x", theme.Id);
        Assert.Empty(theme.Layers);
    }

    [Theory]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("#0B1020", 11, 16, 32)]
    [InlineData("#F00", 255, 0, 0)]
    [InlineData("black", 0, 0, 0)]
    public void Color_Parsing(string spec, byte r, byte g, byte b)
    {
        SkiaSharp.SKColor color = ColorSpec.Parse(spec);

        Assert.Equal(r, color.Red);
        Assert.Equal(g, color.Green);
        Assert.Equal(b, color.Blue);
    }
}

public sealed class BindingExpanderTests
{
    private static SensorSnapshot Snapshot() => new(
        DateTimeOffset.Parse("2026-10-24T21:30:00+08:00"),
        new Dictionary<string, SensorValue>
        {
            ["cpu.temp.package"] = SensorValue.FromNumber(62.4),
            ["fps.current"] = SensorValue.FromNumber(144),
            ["gpu.name"] = SensorValue.FromText("RX 6750 GRE"),
            ["mem.load"] = SensorValue.FromNumber(41.6),
        });

    [Fact]
    public void Expands_Number_With_Format()
    {
        Assert.Equal("62", BindingExpander.Expand("{cpu.temp.package:0}", Snapshot()));
        Assert.Equal("62.4", BindingExpander.Expand("{cpu.temp.package:0.0}", Snapshot()));
    }

    [Fact]
    public void Expands_Text_Sensor()
    {
        Assert.Equal("GPU RX 6750 GRE", BindingExpander.Expand("GPU {gpu.name}", Snapshot()));
    }

    [Fact]
    public void Missing_Sensor_Renders_An_Em_Dash()
    {
        Assert.Equal("\u2014", BindingExpander.Expand("{no.such.sensor}", Snapshot()));
    }

    [Fact]
    public void Expands_Time_Tokens()
    {
        Assert.Equal("21:30", BindingExpander.Expand("{time:HH:mm}", Snapshot()));
        Assert.Equal("2026-10-24", BindingExpander.Expand("{date}", Snapshot()));
    }

    [Fact]
    public void Escaped_Braces_Are_Literals()
    {
        Assert.Equal("{cpu}", BindingExpander.Expand("{{cpu}}", Snapshot()));
    }

    [Fact]
    public void Unclosed_Token_Is_Appended_Verbatim()
    {
        Assert.Equal("a {b", BindingExpander.Expand("a {b", Snapshot()));
    }

    [Fact]
    public void ResolveNumber_Accepts_Token_And_Literal()
    {
        Assert.Equal(144d, BindingExpander.ResolveNumber("{fps.current}", Snapshot()));
        Assert.Equal(12.5d, BindingExpander.ResolveNumber("12.5", Snapshot()));
        Assert.Null(BindingExpander.ResolveNumber("{no.sensor}", Snapshot()));
    }
}
