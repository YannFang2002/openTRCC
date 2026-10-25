using OpenTRCC.Core.Themes;
using SkiaSharp;

namespace OpenTRCC.Core.Rendering;

/// <summary>
/// Built-in animated backgrounds generated at render time: no assets, no licensing,
/// a few hundred draw calls per frame. Presets: aurora, waves, particles, stars, grid.
/// </summary>
public static class ProceduralBackgrounds
{
    public static readonly string[] Presets =
    {
        "aurora", "waves", "particles", "stars", "grid", "plasma", "rain", "ronin", "sprinter",
    };

    public static void Draw(SKCanvas canvas, ThemeBackground background, TimeSpan elapsed, int width, int height)
    {
        float t = (float)(elapsed.TotalSeconds * Math.Max(0.05, background.Speed));
        SKColor[] colors = background.Colors.Select(c => ColorSpec.Parse(c)).ToArray();
        if (colors.Length == 0)
        {
            colors = new[] { new SKColor(11, 16, 32), new SKColor(45, 212, 191) };
        }

        canvas.Clear(colors[0]);

        switch (background.Preset.ToLowerInvariant())
        {
            case "waves":
                DrawWaves(canvas, colors, t, width, height);
                break;
            case "particles":
                DrawParticles(canvas, colors, t, width, height);
                break;
            case "stars":
                DrawStars(canvas, colors, t, width, height);
                break;
            case "grid":
                DrawGrid(canvas, colors, t, width, height);
                break;
            case "plasma":
                DrawPlasma(canvas, colors, t, width, height);
                break;
            case "rain":
                DrawRain(canvas, colors, t, width, height);
                break;
            case "ronin":
                DrawRonin(canvas, colors, t, width, height);
                break;
            case "sprinter":
                DrawSprinter(canvas, colors, t, width, height);
                break;
            default:
                DrawAurora(canvas, colors, t, width, height);
                break;
        }

        if (background.Dim > 0)
        {
            using var dim = new SKPaint
            {
                Color = SKColors.Black.WithAlpha((byte)Math.Clamp(background.Dim * 255, 0, 255)),
            };
            canvas.DrawRect(new SKRect(0, 0, width, height), dim);
        }
    }

    private static void DrawAurora(SKCanvas canvas, SKColor[] colors, float t, int width, int height)
    {
        // Soft colour blobs drifting on sine paths, added on top of the base colour.
        for (int band = 0; band < 3; band++)
        {
            SKColor color = colors[Math.Min(colors.Length - 1, band % colors.Length)];
            float cx = width * (0.5f + 0.32f * MathF.Sin(t * 0.35f + band * 2.1f));
            float cy = height * (0.4f + 0.22f * MathF.Cos(t * 0.27f + band * 1.7f));
            float radius = width * (0.45f + 0.1f * MathF.Sin(t * 0.2f + band));

            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy),
                radius,
                new[] { color.WithAlpha(150), color.WithAlpha(0) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp);

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Shader = shader,
                BlendMode = SKBlendMode.Plus,
            };

            canvas.DrawRect(new SKRect(0, 0, width, height), paint);
        }
    }

    private static void DrawWaves(SKCanvas canvas, SKColor[] colors, float t, int width, int height)
    {
        for (int line = 0; line < 4; line++)
        {
            SKColor color = colors[Math.Min(colors.Length - 1, line % colors.Length)];
            float center = height * (0.62f + 0.07f * line);
            float amplitude = height * 0.07f;

            using var path = new SKPath();
            for (int x = 0; x <= width; x += 4)
            {
                float phase = x / (float)width * MathF.PI * 2.2f + t * (0.8f + line * 0.15f) + line;
                float y = center + MathF.Sin(phase) * amplitude
                                  + MathF.Sin(phase * 1.7f) * amplitude * 0.35f;

                if (x == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            using var stroke = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                Color = color.WithAlpha(200),
            };
            canvas.DrawPath(path, stroke);

            using var fillPath = new SKPath(path);
            fillPath.LineTo(width, height);
            fillPath.LineTo(0, height);
            fillPath.Close();

            using var fill = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = color.WithAlpha(28),
            };
            canvas.DrawPath(fillPath, fill);
        }
    }

    private static void DrawParticles(SKCanvas canvas, SKColor[] colors, float t, int width, int height)
    {
        const int count = 80;
        for (int i = 0; i < count; i++)
        {
            float seed = Hash(i * 37 + 11);
            float seed2 = Hash(i * 91 + 7);
            float speed = 8f + 26f * Hash(i * 13 + 3);
            float radius = 0.8f + 1.8f * Hash(i * 17 + 5);

            float x = (seed * width + t * speed) % (width + 20) - 10;
            float y = seed2 * height + MathF.Sin(t * 0.7f + i) * 6f;

            SKColor color = colors[Math.Min(colors.Length - 1, i % colors.Length)];
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = color.WithAlpha((byte)(90 + 120 * Hash(i * 29 + 2))),
            };
            canvas.DrawCircle(x, y, radius, paint);
        }
    }

    private static void DrawStars(SKCanvas canvas, SKColor[] colors, float t, int width, int height)
    {
        const int count = 90;
        for (int i = 0; i < count; i++)
        {
            float seed = Hash(i * 53 + 3);
            float seed2 = Hash(i * 71 + 9);
            float speed = 6f + 30f * Hash(i * 19 + 1);

            float x = width - (seed * width + t * speed) % (width + 10);
            float y = seed2 * height;
            int trail = 2 + (int)(3 * Hash(i * 23 + 4));

            SKColor color = colors[Math.Min(colors.Length - 1, i % colors.Length)];
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f,
                Color = color.WithAlpha(160),
            };
            canvas.DrawLine(x, y, x + trail * 2, y, paint);
        }
    }

    private static void DrawGrid(SKCanvas canvas, SKColor[] colors, float t, int width, int height)
    {
        SKColor color = colors[Math.Min(colors.Length - 1, 1)];
        float horizon = height * 0.45f;
        float spacing = 22f;

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = color.WithAlpha(70),
        };

        // Horizontal lines scroll toward the viewer.
        for (int i = 0; i < 14; i++)
        {
            float k = ((i + t * 0.6f) % 14) / 14f;
            float y = horizon + (height - horizon) * k * k;
            canvas.DrawLine(0, y, width, y, paint);
        }

        // Vertical lines converge at the centre.
        for (int i = -6; i <= 6; i++)
        {
            float bottomX = width / 2f + i * spacing * 2.2f;
            canvas.DrawLine(width / 2f, horizon, bottomX, height, paint);
        }

        canvas.DrawLine(0, horizon, width, horizon, paint);
    }

    private static void DrawPlasma(SKCanvas canvas, SKColor[] colors, float t, int width, int height)
    {
        // Evaluated at low resolution (80x60) then upscaled: plasma is smooth, so
        // this looks identical to a per-pixel pass at 320x240 but costs ~16x less.
        const int lowWidth = 80;
        const int lowHeight = 60;
        var pixels = new byte[lowWidth * lowHeight * 4];

        for (int y = 0; y < lowHeight; y++)
        {
            for (int x = 0; x < lowWidth; x++)
            {
                float dx = x - 40f;
                float dy = y - 30f;
                float v = MathF.Sin(x * 0.12f + t)
                          + MathF.Sin(y * 0.18f - t * 0.7f)
                          + MathF.Sin((x + y) * 0.09f + t * 0.5f)
                          + MathF.Sin(MathF.Sqrt(dx * dx + dy * dy) * 0.16f - t);

                SKColor color = Palette(colors, (v + 4f) / 8f);
                int index = (y * lowWidth + x) * 4;
                pixels[index] = color.Blue;
                pixels[index + 1] = color.Green;
                pixels[index + 2] = color.Red;
                pixels[index + 3] = 255;
            }
        }

        var info = new SKImageInfo(lowWidth, lowHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var image = SKImage.FromPixelCopy(info, pixels, lowWidth * 4);
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
        canvas.DrawImage(image, new SKRect(0, 0, width, height), paint);
    }

    private static void DrawRain(SKCanvas canvas, SKColor[] colors, float t, int width, int height)
    {
        const int columns = 34;
        float columnWidth = width / (float)columns;
        SKColor trail = colors[Math.Min(colors.Length - 1, 1)];

        for (int i = 0; i < columns; i++)
        {
            float x = columnWidth * (i + 0.5f);
            float speed = 45f + 100f * Hash(i * 11 + 3);
            int length = 10 + (int)(16 * Hash(i * 13 + 5));
            float head = (t * speed + Hash(i * 7 + 1) * (height + 90)) % (height + 90) - 45f;
            float tail = head - length * 6f;

            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(x, head),
                new SKPoint(x, tail),
                new[] { trail.WithAlpha(210), trail.WithAlpha(8) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp);

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = columnWidth * 0.5f,
                Shader = shader,
            };

            canvas.DrawLine(x, head, x, tail, paint);

            if (head > 0 && head < height)
            {
                using var dot = new SKPaint
                {
                    IsAntialias = true,
                    Color = SKColors.White.WithAlpha(230),
                };
                canvas.DrawCircle(x, head,columnWidth * 0.22f, dot);
            }
        }
    }

    private static SKColor Palette(SKColor[] colors, float position)
    {
        if (colors.Length == 1)
        {
            return colors[0];
        }

        float scaled = Math.Clamp(position, 0f, 1f) * (colors.Length - 1);
        int index = Math.Clamp((int)scaled, 0, colors.Length - 2);
        float fraction = scaled - index;
        SKColor a = colors[index];
        SKColor b = colors[index + 1];

        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * fraction),
            (byte)(a.Green + (b.Green - a.Green) * fraction),
            (byte)(a.Blue + (b.Blue - a.Blue) * fraction));
    }

    /// <summary>
    /// Stylised swordsman silhouette in the rain with lightning flashes. Original
    /// artwork drawn from primitives — no third-party character assets.
    /// </summary>
    private static void DrawRonin(SKCanvas canvas, SKColor[] colors, float t, int width, int height)
    {
        SKColor accent = colors[Math.Min(colors.Length - 1, 1)];
        SKColor cold = colors[Math.Min(colors.Length - 1, 2)];

        using (var sky = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, height),
                new[] { colors[0], new SKColor(6, 10, 20) },
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(new SKRect(0, 0, width, height), sky);
        }

        using (var rain = new SKPaint
        {
            IsAntialias = true,
            StrokeWidth = 1f,
            Color = cold.WithAlpha(60),
        })
        {
            for (int i = 0; i < 70; i++)
            {
                float x = Hash(i * 31 + 7) * width;
                float y = (Hash(i * 17 + 3) * height + t * (90 + 60 * Hash(i * 11))) % (height + 40) - 20;
                canvas.DrawLine(x, y, x - 4, y + 14, rain);
            }
        }

        float cycle = (t + 2.2f) % 5.5f;
        float flash = cycle < 0.10f ? 1f - cycle / 0.10f : 0f;
        if (flash > 0.01f)
        {
            using var bolt = new SKPaint { Color = SKColors.White.WithAlpha((byte)(150 * flash)) };
            canvas.DrawRect(new SKRect(0, 0, width, height * 0.6f), bolt);
        }

        canvas.Save();
        canvas.Scale(width / 320f, height / 240f);
        DrawRoninFigure(canvas, accent, cold, t);
        canvas.Restore();

        using (var fog = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, height * 0.78f), new SKPoint(0, height),
                new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 150) },
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(new SKRect(0, height * 0.78f, width, height), fog);
        }
    }

    private static void DrawRoninFigure(SKCanvas canvas, SKColor accent, SKColor cold, float t)
    {
        const float ground = 216f;
        const float cx = 152f;
        float sway = MathF.Sin(t * 1.6f) * 4f;

        var head = new SKPoint(cx, 78);
        var neck = new SKPoint(cx, 94);
        var shoulderL = new SKPoint(cx - 16, 108);
        var shoulderR = new SKPoint(cx + 16, 108);
        var hipL = new SKPoint(cx - 10, 156);
        var hipR = new SKPoint(cx + 10, 156);
        var kneeL = new SKPoint(cx - 20, 188);
        var kneeR = new SKPoint(cx + 22, 188);
        var footL = new SKPoint(cx - 28, ground);
        var footR = new SKPoint(cx + 32, ground);
        var elbowL = new SKPoint(cx - 28, 126);
        var handL = new SKPoint(cx - 36, 148);
        var elbowR = new SKPoint(cx + 32, 118);
        var handR = new SKPoint(cx + 54, 112);

        using var glowLimb = new SKPaint
        {
            IsAntialias = true,
            Color = accent.WithAlpha(170),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 12f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f),
        };

        using var glowFill = new SKPaint
        {
            IsAntialias = true,
            Color = accent.WithAlpha(150),
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f),
        };

        using var bodyLimb = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(4, 6, 10),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 11f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        using var bodyFill = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(4, 6, 10),
            Style = SKPaintStyle.Fill,
        };

        void DrawBody(SKPaint limb, SKPaint fill)
        {
            canvas.DrawLine(neck, new SKPoint(shoulderL.X + 4, shoulderL.Y + 2), limb);
            canvas.DrawLine(shoulderL, elbowL, limb);
            canvas.DrawLine(elbowL, handL, limb);
            canvas.DrawLine(shoulderR, elbowR, limb);
            canvas.DrawLine(elbowR, handR, limb);
            canvas.DrawLine(hipL, kneeL, limb);
            canvas.DrawLine(kneeL, footL, limb);
            canvas.DrawLine(hipR, kneeR, limb);
            canvas.DrawLine(kneeR, footR, limb);

            using var torso = new SKPath();
            torso.MoveTo(shoulderL);
            torso.LineTo(shoulderR);
            torso.LineTo(new SKPoint(hipR.X + 3, hipR.Y));
            torso.LineTo(new SKPoint(hipL.X - 3, hipL.Y));
            torso.Close();

            canvas.DrawPath(torso, fill);
            canvas.DrawCircle(head, 14f, fill);
            canvas.DrawLine(new SKPoint(head.X, head.Y - 12), new SKPoint(head.X + 7, head.Y - 24), limb);
        }

        DrawBody(glowLimb, glowFill);
        DrawBody(bodyLimb, bodyFill);

        // Katana: cyan blade with a soft glow behind it.
        var tip = new SKPoint(cx + 118, 72);
        using (var bladeGlow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 8f,
            StrokeCap = SKStrokeCap.Round,
            Color = cold.WithAlpha(130),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f),
        })
        {
            canvas.DrawLine(handR, tip, bladeGlow);
        }

        using var blade = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3.4f,
            StrokeCap = SKStrokeCap.Round,
            Color = cold,
        };
        canvas.DrawLine(handR, tip, blade);

        // Guard.
        canvas.DrawLine(
            new SKPoint(handR.X - 5, handR.Y - 4),
            new SKPoint(handR.X + 5, handR.Y + 4),
            blade);

        // Cape behind the body.
        using var cape = new SKPath();
        cape.MoveTo(shoulderL);
        cape.CubicTo(
            new SKPoint(cx - 52, 128),
            new SKPoint(cx - 56 + sway, 172),
            new SKPoint(cx - 34 + sway, 208));
        cape.CubicTo(
            new SKPoint(cx - 22 + sway * 0.5f, 186),
            new SKPoint(cx - 12, 168),
            new SKPoint(hipL.X, hipL.Y));
        cape.Close();

        using (var capeFill = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(9, 11, 18),
            Style = SKPaintStyle.Fill,
        })
        {
            canvas.DrawPath(cape, capeFill);
        }

        using var capeRim = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.8f,
            Color = accent.WithAlpha(170),
        };
        canvas.DrawPath(cape, capeRim);
    }

    /// <summary>
    /// Sprinting silhouette against radial speed lines — original artwork, not a
    /// game/anime character.
    /// </summary>
    private static void DrawSprinter(SKCanvas canvas, SKColor[] colors, float t, int width, int height)
    {
        SKColor accent = colors[Math.Min(colors.Length - 1, 1)];
        SKColor warm = colors[Math.Min(colors.Length - 1, 2)];

        canvas.Clear(colors[0]);

        float centreX = width * 0.58f;
        float centreY = height * 0.5f;
        using (var lines = new SKPaint
        {
            IsAntialias = true,
            StrokeWidth = 1.4f,
            Color = accent.WithAlpha(70),
        })
        {
            for (int i = 0; i < 64; i++)
            {
                float phase = Hash(i * 13 + 5) + t * (0.35f + 0.5f * Hash(i * 7 + 1));
                float angle = phase * MathF.PI * 2f;
                float inner = 26f + 18f * Hash(i * 17 + 9);
                float outer = MathF.Max(width, height) * 0.75f;

                float x1 = centreX + MathF.Cos(angle) * inner;
                float y1 = centreY + MathF.Sin(angle) * inner;
                float x2 = centreX + MathF.Cos(angle) * outer;
                float y2 = centreY + MathF.Sin(angle) * outer;

                canvas.DrawLine(x1, y1, x2, y2, lines);
            }
        }

        using (var glow = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(width * 0.35f, height * 0.82f),
                height * 0.6f,
                new[] { warm.WithAlpha(90), warm.WithAlpha(0) },
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(new SKRect(0, 0, width, height), glow);
        }

        canvas.Save();
        canvas.Scale(width / 320f, height / 240f);
        DrawSprinterFigure(canvas, accent, t);
        canvas.Restore();

        using var ground = new SKPaint
        {
            IsAntialias = true,
            StrokeWidth = 1.5f,
            Color = accent.WithAlpha(120),
        };
        canvas.DrawLine(0, height * 0.87f, width, height * 0.87f, ground);
    }

    private static void DrawSprinterFigure(SKCanvas canvas, SKColor accent, float t)
    {
        const float ground = 206f;
        float phase = t * 7.5f;
        float swing = MathF.Sin(phase);
        float swing2 = MathF.Cos(phase);

        var head = new SKPoint(188, 64);
        var neck = new SKPoint(182, 78);
        var shoulderA = new SKPoint(172, 88);
        var shoulderB = new SKPoint(190, 96);
        var hipA = new SKPoint(150, 136);
        var hipB = new SKPoint(166, 144);

        var kneeFront = new SKPoint(192 + 6 * swing, 168);
        var footFront = new SKPoint(206 + 10 * swing, ground - MathF.Max(0, swing) * 16);
        var kneeBack = new SKPoint(124 - 4 * swing, 172);
        var footBack = new SKPoint(104 - 8 * swing, ground - MathF.Max(0, -swing) * 12);

        var elbowFront = new SKPoint(200, 112);
        var handFront = new SKPoint(214, 128 + 7 * swing);
        var elbowBack = new SKPoint(148, 108);
        var handBack = new SKPoint(126, 94 - 7 * swing2);

        using var glowLimb = new SKPaint
        {
            IsAntialias = true,
            Color = accent.WithAlpha(170),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 13f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f),
        };

        using var glowFill = new SKPaint
        {
            IsAntialias = true,
            Color = accent.WithAlpha(160),
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f),
        };

        using var bodyLimb = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(4, 8, 12),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 10f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        using var bodyFill = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(4, 8, 12),
            Style = SKPaintStyle.Fill,
        };

        void DrawBody(SKPaint limb, SKPaint fill)
        {
            canvas.DrawLine(neck, shoulderA, limb);
            canvas.DrawLine(shoulderA, elbowFront, limb);
            canvas.DrawLine(elbowFront, handFront, limb);
            canvas.DrawLine(shoulderB, elbowBack, limb);
            canvas.DrawLine(elbowBack, handBack, limb);
            canvas.DrawLine(hipA, kneeFront, limb);
            canvas.DrawLine(kneeFront, footFront, limb);
            canvas.DrawLine(hipB, kneeBack, limb);
            canvas.DrawLine(kneeBack, footBack, limb);

            using var torso = new SKPath();
            torso.MoveTo(shoulderA);
            torso.LineTo(shoulderB);
            torso.LineTo(hipB);
            torso.LineTo(hipA);
            torso.Close();

            canvas.DrawPath(torso, fill);
            canvas.DrawCircle(head, 11.5f, fill);
        }

        DrawBody(glowLimb, glowFill);
        DrawBody(bodyLimb, bodyFill);

        using (var headRim = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            Color = accent.WithAlpha(210),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f),
        })
        {
            canvas.DrawCircle(head, 11.5f, headRim);
        }

        using var streaks = new SKPaint
        {
            IsAntialias = true,
            StrokeWidth = 2f,
            Color = accent.WithAlpha(90),
        };
        for (int i = 0; i < 6; i++)
        {
            float y = ground - 44 - i * 13 + MathF.Sin(t * 4 + i) * 3;
            float length = 30 + 26 * Hash(i * 9 + 2);
            canvas.DrawLine(hipA.X - 60 - length, y, hipA.X - 60, y, streaks);
        }
    }

    /// <summary>Deterministic pseudo-random in [0,1) — keeps frames reproducible per index.</summary>
    private static float Hash(int value)    {
        uint x = (uint)value * 2654435761u;
        x ^= x >> 13;
        x *= 2246822519u;
        x ^= x >> 16;
        return (x & 0xFFFF) / 65536f;
    }
}
