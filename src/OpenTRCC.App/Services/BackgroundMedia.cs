using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenTRCC.Core.Imaging;
using OpenTRCC.Core.Rendering;
using OpenTRCC.Core.Themes;

namespace OpenTRCC.App.Services;

/// <summary>
/// Animation support for image backgrounds. GIF is decoded here (WPF's
/// GifBitmapDecoder) and handed to the renderer frame by frame; the same
/// interface will serve video frames later.
/// </summary>
public static class BackgroundMedia
{
    public const int MaxGifFrames = 240;

    /// <summary>
    /// Returns a frame provider when the theme's background asset is an animated
    /// GIF or a video file; null for static images and solid/gradient backgrounds.
    /// </summary>
    public static IBackgroundFrameProvider? CreateFor(ThemeDocument theme, string baseDirectory)
    {
        ThemeBackground background = theme.Canvas.Background;
        if (background.Kind != BackgroundKind.Image || string.IsNullOrWhiteSpace(background.Asset))
        {
            return null;
        }

        string path = Path.IsPathRooted(background.Asset)
            ? background.Asset
            : Path.Combine(baseDirectory, background.Asset);

        if (!File.Exists(path))
        {
            return null;
        }

        // Pre-extracted JPEG frame sequence (video import).
        if (path.EndsWith("meta.json", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty)
                .EndsWith(".frames", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return new JpegSequenceProvider(path);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                Log.Write($"frame sequence load failed: {path}: {ex.Message}");
                return null;
            }
        }

        if (VideoBackground.IsVideo(path))
        {
            return VideoBackground.Create(path);
        }

        if (!path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return new GifFrameProvider(path);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FormatException)
        {
            Log.Write($"gif load failed: {path}: {ex.Message}");
            return null;
        }
    }

    private sealed class GifFrameProvider : IBackgroundFrameProvider, IDisposable
    {
        private readonly List<BgraImage> _frames = new();
        private readonly List<TimeSpan> _starts = new();
        private TimeSpan _duration = TimeSpan.FromSeconds(1);
        private int _lastIndex = -1;

        public GifFrameProvider(string path)
        {
            var decoder = new GifBitmapDecoder(
                new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

            double cumulative = 0;
            foreach (BitmapFrame frame in decoder.Frames)
            {
                if (_frames.Count >= MaxGifFrames)
                {
                    break;
                }

                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                var pixels = new byte[width * height * 4];
                converted.CopyPixels(pixels, width * 4, 0);

                _frames.Add(new BgraImage(width, height, pixels));
                _starts.Add(TimeSpan.FromSeconds(cumulative));
                cumulative += FrameDelay(frame);
            }

            if (_frames.Count > 0)
            {
                _duration = TimeSpan.FromSeconds(Math.Max(0.05, cumulative));
            }
        }

        public bool TryGetFrame(TimeSpan elapsed, out BgraImage frame)
        {
            frame = null!;
            if (_frames.Count == 0)
            {
                return false;
            }

            if (_frames.Count == 1)
            {
                frame = _frames[0];
                return true;
            }

            double position = elapsed.TotalSeconds % _duration.TotalSeconds;
            int index = 0;
            for (int i = 0; i < _starts.Count; i++)
            {
                if (_starts[i].TotalSeconds <= position)
                {
                    index = i;
                }
                else
                {
                    break;
                }
            }

            _lastIndex = index;
            frame = _frames[index];
            return true;
        }

        public void Dispose() => _frames.Clear();

        private static double FrameDelay(BitmapFrame frame)
        {
            const double fallback = 0.1; // 100 ms, the GIF default
            if (frame.Metadata is not BitmapMetadata metadata)
            {
                return fallback;
            }

            try
            {
                object? value = metadata.GetQuery("/grctlext/Delay");
                if (value is ushort hundredths && hundredths > 0)
                {
                    return hundredths / 100.0;
                }
            }
            catch (NotSupportedException)
            {
            }

            return fallback;
        }
    }
}
