using OpenTRCC.Core.Imaging;
using SkiaSharp;

namespace OpenTRCC.Core.Rendering;

/// <summary>What the probe wants to see on screen for one test frame.</summary>
public sealed record TestPatternOptions
{
    public int Width { get; init; } = 320;

    public int Height { get; init; } = 240;

    public string Title { get; init; } = "OpenTRCC";

    public int ProductId { get; init; } = -1;

    public int SubVersion { get; init; } = -1;

    public string Mode { get; init; } = "rgb565";

    public int Rotation { get; init; } = 90;

    public string ByteOrder { get; init; } = "le";

    public int ChunkSize { get; init; } = 512;

    /// <summary>0..1 animation phase; drives the moving vertical bar.</summary>
    public double Phase { get; init; }
}

/// <summary>
/// Diagnostic image: four labeled colour quadrants, an up arrow, a centred title
/// and a moving bar. It answers orientation, mirroring, colour order and refresh
/// in one look.
/// </summary>
public static class TestPatternRenderer
{
    private static readonly SKColor Background = new(11, 16, 32);

    private static readonly SKColor[] Quadrants =
    {
        new(220, 40, 40),   // top-left
        new(40, 200, 90),   // top-right
        new(40, 90, 230),   // bottom-left
        new(240, 210, 40),  // bottom-right
    };

    private static readonly string[] QuadrantLabels = { "TL", "TR", "BL", "BR" };

    public static SKBitmap Render(TestPatternOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var bitmap = new SKBitmap(new SKImageInfo(
            options.Width, options.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Background);

        float halfWidth = options.Width / 2f;
        float halfHeight = options.Height / 2f;

        using (var fill = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill })
        {
            fill.Color = Quadrants[0];
            canvas.DrawRect(new SKRect(0, 0, halfWidth, halfHeight), fill);
            fill.Color = Quadrants[1];
            canvas.DrawRect(new SKRect(halfWidth, 0, options.Width, halfHeight), fill);
            fill.Color = Quadrants[2];
            canvas.DrawRect(new SKRect(0, halfHeight, halfWidth, options.Height), fill);
            fill.Color = Quadrants[3];
            canvas.DrawRect(new SKRect(halfWidth, halfHeight, options.Width, options.Height), fill);

            // Centre bands so text stays readable over the quadrants.
            fill.Color = new SKColor(0, 0, 0, 190);
            canvas.DrawRect(new SKRect(0, halfHeight - 46, options.Width, halfHeight + 30), fill);

            // Moving bar: proves the frame is being refreshed rather than frozen.
            float barX = (float)(options.Phase * (options.Width - 10));
            fill.Color = SKColors.White;
            canvas.DrawRect(new SKRect(barX, 0, barX + 10, options.Height), fill);
        }

        DrawCentredText(canvas, options.Title + " 320x240", halfWidth, halfHeight - 22, 26, SKColors.White, bold: true);
        DrawCentredText(
            canvas,
            $"PM={options.ProductId} SUB={options.SubVersion} rot={options.Rotation} {options.ByteOrder} chunk={options.ChunkSize}",
            halfWidth,
            halfHeight + 6,
            14,
            new SKColor(226, 232, 240));

        DrawCentredText(canvas, options.Mode, halfWidth, halfHeight + 24, 12, new SKColor(148, 163, 184));

        DrawUpArrow(canvas, halfWidth, 58);
        DrawQuadrantLabels(canvas, options);

        return bitmap;
    }

    public static BgraImage RenderBgra(TestPatternOptions options)
    {
        using SKBitmap bitmap = Render(options);
        return ToBgra(bitmap);
    }

    public static BgraImage ToBgra(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        int width = bitmap.Width;
        int height = bitmap.Height;
        SKBitmap? converted = null;

        try
        {
            SKBitmap source = bitmap;
            if (bitmap.ColorType != SKColorType.Bgra8888)
            {
                converted = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
                if (!bitmap.CopyTo(converted, SKColorType.Bgra8888))
                {
                    throw new InvalidOperationException("Failed to convert the Skia bitmap to BGRA8888.");
                }

                source = converted;
            }

            var pixels = new byte[width * height * 4];
            source.GetPixelSpan().CopyTo(pixels);
            return new BgraImage(width, height, pixels);
        }
        finally
        {
            converted?.Dispose();
        }
    }

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Copies a BGRA canvas into a new Skia bitmap (used before JPEG encoding).</summary>
    public static SKBitmap FromBgra(BgraImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKImage skImage = SKImage.FromPixelCopy(info, image.Pixels, image.Width * 4);
        var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawImage(skImage, 0, 0);
        }

        return bitmap;
    }

    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality = 85)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    private static void DrawCentredText(
        SKCanvas canvas, string text, float x, float y, float size, SKColor color, bool bold = false)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            TextSize = size,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName(
                "Segoe UI",
                bold ? SKFontStyleWeight.SemiBold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                SKFontStyleSlant.Upright),
        };

        // Outline for legibility on any background.
        using var outline = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true,
            TextSize = size,
            TextAlign = SKTextAlign.Center,
            Typeface = paint.Typeface,
        };

        canvas.DrawText(text, x, y, outline);
        canvas.DrawText(text, x, y, paint);
    }

    private static void DrawUpArrow(SKCanvas canvas, float centreX, float top)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Fill };
        using var path = new SKPath();
        path.MoveTo(centreX, top);
        path.LineTo(centreX - 22, top + 34);
        path.LineTo(centreX + 22, top + 34);
        path.Close();
        canvas.DrawPath(path, paint);

        using var stem = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(centreX - 7, top + 30, centreX + 7, top + 52), stem);
    }

    private static void DrawQuadrantLabels(SKCanvas canvas, TestPatternOptions options)
    {
        float inset = 10;
        DrawCorner(canvas, QuadrantLabels[0], inset, inset, SKTextAlign.Left, options);
        DrawCorner(canvas, QuadrantLabels[1], options.Width - inset, inset, SKTextAlign.Right, options);
        DrawCorner(canvas, QuadrantLabels[2], inset, options.Height - inset - 12, SKTextAlign.Left, options);
        DrawCorner(canvas, QuadrantLabels[3], options.Width - inset, options.Height - inset - 12, SKTextAlign.Right, options);
    }

    private static void DrawCorner(
        SKCanvas canvas, string label, float x, float y, SKTextAlign align, TestPatternOptions options)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            TextSize = 13,
            TextAlign = align,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            Style = SKPaintStyle.Fill,
        };

        using var outline = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            TextSize = 13,
            TextAlign = align,
            Typeface = paint.Typeface,
        };

        canvas.DrawText(label, x, y + 14, outline);
        canvas.DrawText(label, x, y + 14, paint);
    }
}
