using OpenTRCC.Core.Binding;
using OpenTRCC.Core.Data;
using OpenTRCC.Core.Imaging;
using OpenTRCC.Core.Themes;
using SkiaSharp;

namespace OpenTRCC.Core.Rendering;

/// <summary>Draws a theme into a BGRA bitmap. One instance per display target; it owns sample history.</summary>
public sealed class ThemeRenderer
{
    private readonly Dictionary<string, Queue<double>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SKBitmap?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _updatedThisFrame = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastTimestamp = DateTimeOffset.MinValue;
    private bool _newData;

    /// <summary>Directory used to resolve relative image assets (normally the theme file's folder).</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>Animated background frames (GIF now, video later). Null for static themes.</summary>
    public IBackgroundFrameProvider? BackgroundFrames { get; set; }

    public SKBitmap Render(ThemeDocument theme, SensorSnapshot snapshot, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(snapshot);

        int width = theme.Canvas.Width;
        int height = theme.Canvas.Height;

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        DrawBackground(canvas, theme.Canvas.Background, width, height, elapsed);
        _updatedThisFrame.Clear();

        // History belongs to the data rate (1 Hz), not the render rate: appending
        // on every frame makes the sparkline drift and the content hash change
        // 20x/s, which defeats FramePump and floods USB.
        _newData = snapshot.Timestamp != _lastTimestamp;
        _lastTimestamp = snapshot.Timestamp;

        foreach (Layer layer in theme.Layers)
        {
            if (!layer.Visible)
            {
                continue;
            }

            DrawLayer(canvas, layer, snapshot, elapsed);
        }

        return bitmap;
    }

    public void ForgetHistory() => _history.Clear();

    private void DrawLayer(SKCanvas canvas, Layer layer, SensorSnapshot snapshot, TimeSpan elapsed)
    {
        switch (layer)
        {
            case TextLayer text:
                DrawText(canvas, text, snapshot);
                break;
            case BarLayer bar:
                DrawBar(canvas, bar, snapshot);
                break;
            case RingLayer ring:
                DrawRing(canvas, ring, snapshot);
                break;
            case SparklineLayer spark:
                DrawSparkline(canvas, spark, snapshot);
                break;
            case ImageLayer image:
                DrawImage(canvas, image);
                break;
        }
    }

    private void DrawBackground(SKCanvas canvas, ThemeBackground background, int width, int height, TimeSpan elapsed)
    {
        switch (background.Kind)
        {
            case BackgroundKind.Solid:
                canvas.Clear(ColorSpec.Parse(background.Colors.FirstOrDefault(), SKColors.Black));
                break;

            case BackgroundKind.Gradient:
                SKColor[] colors = background.Colors.Select(c => ColorSpec.Parse(c)).ToArray();
                if (colors.Length == 0)
                {
                    canvas.Clear(SKColors.Black);
                    break;
                }

                if (colors.Length == 1)
                {
                    canvas.Clear(colors[0]);
                    break;
                }

                double radians = background.Angle * Math.PI / 180.0;
                float dx = (float)Math.Cos(radians);
                float dy = (float)Math.Sin(radians);
                var start = new SKPoint(width / 2f - dx * width / 2f, height / 2f - dy * height / 2f);
                var end = new SKPoint(width / 2f + dx * width / 2f, height / 2f + dy * height / 2f);

                using (var paint = new SKPaint
                {
                    IsAntialias = true,
                    Shader = SKShader.CreateLinearGradient(start, end, colors, null, SKShaderTileMode.Clamp),
                })
                {
                    canvas.DrawRect(new SKRect(0, 0, width, height), paint);
                }

                break;

            case BackgroundKind.Image:
                DrawImageFill(canvas, background, width, height, elapsed);
                break;

            case BackgroundKind.Procedural:
                ProceduralBackgrounds.Draw(canvas, background, elapsed, width, height);
                break;
        }
    }

    private void DrawImageFill(SKCanvas canvas, ThemeBackground background, int width, int height, TimeSpan elapsed)
    {
        // Animated sources (GIF, later video) win over the static asset.
        if (BackgroundFrames is not null
            && BackgroundFrames.TryGetFrame(elapsed, out BgraImage frame)
            && frame.Width > 0 && frame.Height > 0)
        {
            DrawBgraFill(canvas, frame, background, width, height);
            return;
        }

        SKBitmap? bitmap = LoadImage(background.Asset);
        if (bitmap is null)
        {
            canvas.Clear(SKColors.Black);
            return;
        }

        var destination = Offset(FitRect(bitmap.Width, bitmap.Height, new SKRect(0, 0, width, height), background.Fit, background.Zoom), background, width, height);

        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.DrawBitmap(bitmap, destination, paint);

            if (background.Dim > 0)
            {
                using var dim = new SKPaint
                {
                    Color = SKColors.Black.WithAlpha((byte)Math.Clamp(background.Dim * 255, 0, 255)),
                };
                canvas.DrawRect(new SKRect(0, 0, width, height), dim);
            }
        }
    }

    private void DrawBgraFill(
        SKCanvas canvas, BgraImage frame, ThemeBackground background, int width, int height)
    {
        using SKBitmap bitmap = TestPatternRenderer.FromBgra(frame);
        var destination = Offset(FitRect(frame.Width, frame.Height, new SKRect(0, 0, width, height), background.Fit, background.Zoom), background, width, height);

        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.DrawBitmap(bitmap, destination, paint);

            if (background.Dim > 0)
            {
                using var dim = new SKPaint
                {
                    Color = SKColors.Black.WithAlpha((byte)Math.Clamp(background.Dim * 255, 0, 255)),
                };
                canvas.DrawRect(new SKRect(0, 0, width, height), dim);
            }
        }
    }

    private void DrawText(SKCanvas canvas, TextLayer layer, SensorSnapshot snapshot)
    {
        string text = BindingExpander.Expand(layer.Text, snapshot);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (layer.HideWhenMissing && text.Contains('\u2014'))
        {
            return;
        }

        SKColor color = ColorSpec.Parse(layer.Color, SKColors.White).WithAlpha(Alpha(layer.Opacity));

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = color,
            TextSize = (float)layer.Font.Size,
            Typeface = TypefaceFor(layer.Font),
            TextAlign = layer.Align.ToLowerInvariant() switch
            {
                "center" => SKTextAlign.Center,
                "right" => SKTextAlign.Right,
                _ => SKTextAlign.Left,
            },
            SubpixelText = true,
            LcdRenderText = false,
        };

        float x = layer.Align.ToLowerInvariant() switch
        {
            "center" => (float)(layer.X + layer.W / 2),
            "right" => (float)(layer.X + layer.W),
            _ => (float)layer.X,
        };

        SKFontMetrics metrics = paint.FontMetrics;
        float baseline = layer.VerticalAlign.ToLowerInvariant() switch
        {
            "middle" => (float)(layer.Y + (layer.H - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent),
            "bottom" => (float)(layer.Y + layer.H - metrics.Descent),
            _ => (float)layer.Y - metrics.Ascent,
        };

        if (layer.Shadow)
        {
            using var shadow = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.Black.WithAlpha((byte)(color.Alpha * 0.7)),
                TextSize = (float)layer.Font.Size,
                Typeface = paint.Typeface,
                TextAlign = paint.TextAlign,
                SubpixelText = true,
            };
            canvas.DrawText(text, x + 2, baseline + 2, shadow);
        }

        canvas.DrawText(text, x, baseline, paint);
    }

    private void DrawBar(SKCanvas canvas, BarLayer layer, SensorSnapshot snapshot)
    {
        double value = BindingExpander.ResolveNumber(layer.Value, snapshot) ?? double.NaN;
        double fraction = double.IsNaN(value) || layer.Max <= layer.Min
            ? 0
            : Math.Clamp((value - layer.Min) / (layer.Max - layer.Min), 0, 1);

        SKColor track = ColorSpec.Parse(layer.Track).WithAlpha(Alpha(layer.Opacity));
        SKColor fill = ColorSpec.Parse(layer.Fill).WithAlpha(Alpha(layer.Opacity));
        float radius = (float)Math.Max(0, layer.Radius);

        var box = new SKRect((float)layer.X, (float)layer.Y, (float)(layer.X + layer.W), (float)(layer.Y + layer.H));

        using (var paint = new SKPaint { IsAntialias = true, Color = track })
        {
            canvas.DrawRoundRect(box, radius, radius, paint);
        }

        if (fraction <= 0)
        {
            return;
        }

        bool vertical = layer.Orientation.Equals("vertical", StringComparison.OrdinalIgnoreCase);
        SKRect filled = vertical
            ? new SKRect(box.Left, (float)(box.Bottom - box.Height * fraction), box.Right, box.Bottom)
            : new SKRect(box.Left, box.Top, (float)(box.Left + box.Width * fraction), box.Bottom);

        using var fillPaint = new SKPaint { IsAntialias = true, Color = fill };
        canvas.DrawRoundRect(filled, radius, radius, fillPaint);
    }

    private void DrawRing(SKCanvas canvas, RingLayer layer, SensorSnapshot snapshot)
    {
        double value = BindingExpander.ResolveNumber(layer.Value, snapshot) ?? double.NaN;
        double fraction = double.IsNaN(value) || layer.Max <= layer.Min
            ? 0
            : Math.Clamp((value - layer.Min) / (layer.Max - layer.Min), 0, 1);

        float centerX = (float)(layer.X + layer.W / 2);
        float centerY = (float)(layer.Y + layer.H / 2);
        float radius = (float)(Math.Min(layer.W, layer.H) / 2 - layer.Thickness / 2);
        if (radius <= 0)
        {
            return;
        }

        var oval = new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);
        float start = (float)layer.StartAngle;
        float sweep = (float)layer.SweepAngle;
        SKStrokeCap cap = layer.Cap.Equals("butt", StringComparison.OrdinalIgnoreCase)
            ? SKStrokeCap.Butt
            : SKStrokeCap.Round;

        using (var trackPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)layer.Thickness,
            StrokeCap = cap,
            Color = ColorSpec.Parse(layer.Track).WithAlpha(Alpha(layer.Opacity)),
        })
        {
            canvas.DrawArc(oval, start, sweep, useCenter: false, trackPaint);
        }

        if (fraction <= 0)
        {
            return;
        }

        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)layer.Thickness,
            StrokeCap = cap,
        };

        float arcSweep = (float)(sweep * fraction);
        byte alpha = Alpha(layer.Opacity);

        if (layer.Fill.Count <= 1)
        {
            fillPaint.Color = ColorSpec.Parse(layer.Fill.FirstOrDefault()).WithAlpha(alpha);
            canvas.DrawArc(oval, start, arcSweep, useCenter: false, fillPaint);
            return;
        }

        // Sweep gradient aligned with the arc: teal at the start, red at the top.
        SKColor[] colors = layer.Fill.Select(c => ColorSpec.Parse(c).WithAlpha(alpha)).ToArray();
        float span = Math.Clamp(sweep / 360f, 0.01f, 1f);
        var positions = new float[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            positions[i] = colors.Length == 1 ? 0 : span * i / (colors.Length - 1);
        }

        using SKShader shader = SKShader.CreateSweepGradient(
            new SKPoint(centerX, centerY), colors, positions);
        using SKShader aligned = shader.WithLocalMatrix(
            SKMatrix.CreateRotationDegrees(start, centerX, centerY));
        fillPaint.Shader = aligned;

        canvas.DrawArc(oval, start, arcSweep, useCenter: false, fillPaint);
    }

    private void DrawSparkline(SKCanvas canvas, SparklineLayer layer, SensorSnapshot snapshot)
    {
        string source = layer.Source.Trim().TrimStart('{').TrimEnd('}');
        Queue<double> samples = History(source, layer.Samples);

        if (_newData && _updatedThisFrame.Add(source))
        {
            double? value = BindingExpander.ResolveNumber(layer.Source, snapshot);
            if (value is double number)
            {
                samples.Enqueue(number);
            }

            while (samples.Count > layer.Samples)
            {
                samples.Dequeue();
            }
        }

        if (samples.Count < 2)
        {
            return;
        }

        double min = layer.Min;
        double max = layer.Max;
        if (max <= min)
        {
            max = Math.Max(min + 1, samples.Max());
        }

        float left = (float)layer.X;
        float top = (float)layer.Y;
        float width = (float)layer.W;
        float height = (float)layer.H;

        using var path = new SKPath();
        bool first = true;
        int index = 0;
        int last = samples.Count - 1;

        foreach (double sample in samples)
        {
            float x = left + width * (last == 0 ? 0 : index / (float)last);
            float normalized = (float)Math.Clamp((sample - min) / (max - min), 0, 1);
            float y = top + height - height * normalized;

            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }

            index++;
        }

        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)layer.LineWidth,
            StrokeJoin = SKStrokeJoin.Round,
            Color = ColorSpec.Parse(layer.Stroke).WithAlpha(Alpha(layer.Opacity)),
        };

        canvas.DrawPath(path, stroke);

        if (!string.IsNullOrEmpty(layer.Fill))
        {
            using var area = new SKPath(path);
            area.LineTo(left + width, top + height);
            area.LineTo(left, top + height);
            area.Close();

            using var fill = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = ColorSpec.Parse(layer.Fill).WithAlpha((byte)(Alpha(layer.Opacity) * 0.35)),
            };

            canvas.DrawPath(area, fill);
        }
    }

    private void DrawImage(SKCanvas canvas, ImageLayer layer)
    {
        SKBitmap? bitmap = LoadImage(layer.Asset);
        if (bitmap is null)
        {
            return;
        }

        var box = new SKRect((float)layer.X, (float)layer.Y, (float)(layer.X + layer.W), (float)(layer.Y + layer.H));
        SKRect destination = FitRect(bitmap.Width, bitmap.Height, box, layer.Fit);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White.WithAlpha(Alpha(layer.Opacity)),
        };

        if (!string.IsNullOrEmpty(layer.Tint))
        {
            paint.ColorFilter = SKColorFilter.CreateBlendMode(
                ColorSpec.Parse(layer.Tint), SKBlendMode.Modulate);
        }

        canvas.DrawBitmap(bitmap, destination, paint);
    }

    private SKBitmap? LoadImage(string? asset)
    {
        if (string.IsNullOrWhiteSpace(asset))
        {
            return null;
        }

        string path = Path.IsPathRooted(asset) ? asset : Path.Combine(BaseDirectory ?? ".", asset);
        if (!_imageCache.TryGetValue(path, out SKBitmap? bitmap))
        {
            bitmap = File.Exists(path) ? SKBitmap.Decode(path) : null;
            _imageCache[path] = bitmap;
        }

        return bitmap;
    }

    private static SKRect FitRect(int sourceWidth, int sourceHeight, SKRect box, string fit, double zoom = 1)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return box;
        }

        switch (fit.ToLowerInvariant())
        {
            case "stretch":
                return ScaleAroundCenter(box, zoom);

            case "cover":
            {
                float scale = Math.Max(box.Width / sourceWidth, box.Height / sourceHeight);
                float width = sourceWidth * scale;
                float height = sourceHeight * scale;
                return ScaleAroundCenter(new SKRect(
                    box.MidX - width / 2, box.MidY - height / 2,
                    box.MidX + width / 2, box.MidY + height / 2), zoom);
            }

            default: // contain
            {
                float scale = Math.Min(box.Width / sourceWidth, box.Height / sourceHeight);
                float width = sourceWidth * scale;
                float height = sourceHeight * scale;
                return ScaleAroundCenter(new SKRect(
                    box.MidX - width / 2, box.MidY - height / 2,
                    box.MidX + width / 2, box.MidY + height / 2), zoom);
            }
        }
    }

    private static SKRect Offset(SKRect rect, ThemeBackground background, int width, int height)
    {
        float left = rect.Left + (float)background.OffsetX;
        float top = rect.Top + (float)background.OffsetY;

        // In cover mode the picture must keep covering the canvas: clamp the pan so
        // dragging never exposes a black edge.
        if (background.Fit.Equals("cover", StringComparison.OrdinalIgnoreCase))
        {
            if (rect.Width >= width)
            {
                left = Math.Clamp(left, width - rect.Width, 0);
            }

            if (rect.Height >= height)
            {
                top = Math.Clamp(top, height - rect.Height, 0);
            }
        }

        return new SKRect(left, top, left + rect.Width, top + rect.Height);
    }

    private static SKRect ScaleAroundCenter(SKRect rect, double zoom)
    {
        if (Math.Abs(zoom - 1) < 0.001)
        {
            return rect;
        }

        float halfWidth = (float)(rect.Width * zoom / 2);
        float halfHeight = (float)(rect.Height * zoom / 2);
        return new SKRect(
            rect.MidX - halfWidth, rect.MidY - halfHeight,
            rect.MidX + halfWidth, rect.MidY + halfHeight);
    }

    private Queue<double> History(string source, int capacity)
    {
        if (!_history.TryGetValue(source, out Queue<double>? queue))
        {
            queue = new Queue<double>(Math.Max(2, capacity));
            _history[source] = queue;
        }

        return queue;
    }

    // Typefaces are expensive and immutable; recreating one per text layer per
    // frame (20 fps × several layers) churns Skia's font cache and was a source
    // of text occasionally missing from a frame.
    private static readonly Dictionary<(string Family, int Weight), SKTypeface> TypefaceCache = new();
    private static readonly object TypefaceGate = new();

    private static SKTypeface TypefaceFor(FontSpec font)
    {
        int bucket = font.Weight switch
        {
            >= 700 => 700,
            >= 600 => 600,
            >= 500 => 500,
            _ => 400,
        };

        var key = (font.Family, bucket);
        lock (TypefaceGate)
        {
            if (TypefaceCache.TryGetValue(key, out SKTypeface? cached))
            {
                return cached;
            }

            SKFontStyleWeight weight = bucket switch
            {
                700 => SKFontStyleWeight.Bold,
                600 => SKFontStyleWeight.SemiBold,
                500 => SKFontStyleWeight.Medium,
                _ => SKFontStyleWeight.Normal,
            };

            SKTypeface typeface = SKTypeface.FromFamilyName(
                font.Family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                ?? SKTypeface.Default;

            TypefaceCache[key] = typeface;
            return typeface;
        }
    }

    private static byte Alpha(double opacity) => (byte)Math.Clamp(opacity * 255, 0, 255);
}
