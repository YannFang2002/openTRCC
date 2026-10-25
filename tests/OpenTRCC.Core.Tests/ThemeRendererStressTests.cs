using OpenTRCC.Core.Data;
using OpenTRCC.Core.Rendering;
using OpenTRCC.Core.Themes;
using SkiaSharp;
using Xunit;

namespace OpenTRCC.Core.Tests;

public sealed class ThemeRendererStressTests
{
    private static SensorSnapshot Snapshot() => new(
        DateTimeOffset.Parse("2026-10-24T23:00:00+08:00"),
        new Dictionary<string, SensorValue>
        {
            ["fps.current"] = SensorValue.FromNumber(180),
        });

    [Fact]
    public void Big_Number_Is_Drawn_In_Every_Frame()
    {
        var theme = new ThemeDocument
        {
            Canvas = new ThemeCanvas
            {
                Width = 320,
                Height = 240,
                Background = new ThemeBackground { Kind = BackgroundKind.Solid, Colors = new[] { "#000000" } },
            },
            Layers = new Layer[]
            {
                new TextLayer
                {
                    X = 0, Y = 30, W = 320, H = 120,
                    Text = "{fps.current:0}",
                    Font = new FontSpec { Family = "Consolas", Size = 92, Weight = 700 },
                    Color = "#34D399",
                    Align = "center",
                    VerticalAlign = "middle",
                },
            },
        };

        var renderer = new ThemeRenderer();
        SensorSnapshot snapshot = Snapshot();
        int minimumLit = int.MaxValue;

        for (int i = 0; i < 60; i++)
        {
            using SKBitmap bitmap = renderer.Render(theme, snapshot, TimeSpan.FromMilliseconds(i * 50));
            int lit = 0;
            for (int y = 30; y < 150; y++)
            {
                for (int x = 0; x < 320; x++)
                {
                    SKColor color = bitmap.GetPixel(x, y);
                    if (color.Green > 150 && color.Red < 120)
                    {
                        lit++;
                    }
                }
            }

            minimumLit = Math.Min(minimumLit, lit);
        }

        // A single blank frame in 60 renders is exactly the "number flickers" bug.
        Assert.True(minimumLit > 1000, $"a frame lost its digits (min lit pixels = {minimumLit})");
    }
}
