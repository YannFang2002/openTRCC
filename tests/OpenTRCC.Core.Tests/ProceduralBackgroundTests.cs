using OpenTRCC.Core.Data;
using OpenTRCC.Core.Rendering;
using OpenTRCC.Core.Themes;
using SkiaSharp;
using Xunit;

namespace OpenTRCC.Core.Tests;

public sealed class ProceduralBackgroundTests
{
    private static ThemeDocument ThemeFor(string preset) => new()
    {
        Id = $"bg-{preset}",
        Canvas = new ThemeCanvas
        {
            Width = 160,
            Height = 120,
            Background = new ThemeBackground
            {
                Kind = BackgroundKind.Procedural,
                Preset = preset,
                Speed = 1,
                Colors = new[] { "#050912", "#2DD4BF", "#6366F1", "#A855F7" },
            },
        },
    };

    [Theory]
    [InlineData("aurora")]
    [InlineData("waves")]
    [InlineData("particles")]
    [InlineData("stars")]
    [InlineData("grid")]
    [InlineData("plasma")]
    [InlineData("rain")]
    [InlineData("ronin")]
    [InlineData("sprinter")]
    public void Every_Preset_Renders_And_Animates(string preset)
    {
        var renderer = new ThemeRenderer();
        ThemeDocument theme = ThemeFor(preset);
        SensorSnapshot snapshot = SensorSnapshot.Sample();

        using SKBitmap first = renderer.Render(theme, snapshot, TimeSpan.Zero);
        using SKBitmap later = renderer.Render(theme, snapshot, TimeSpan.FromSeconds(1.5));

        Assert.Equal(160, first.Width);

        var a = new SKColor[first.Width * first.Height];
        var b = new SKColor[later.Width * later.Height];
        for (int y = 0; y < first.Height; y++)
        {
            for (int x = 0; x < first.Width; x++)
            {
                a[y * first.Width + x] = first.GetPixel(x, y);
                b[y * later.Width + x] = later.GetPixel(x, y);
            }
        }

        Assert.NotEqual(a, b);
    }
}
