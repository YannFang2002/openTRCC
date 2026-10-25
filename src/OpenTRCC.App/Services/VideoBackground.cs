using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenTRCC.Core.Imaging;
using OpenTRCC.Core.Rendering;
using OpenTRCC.Core.Themes;

namespace OpenTRCC.App.Services;

/// <summary>
/// Video background over Windows Media Foundation, reached through WPF's
/// MediaPlayer: the player decodes (hardware where available) and each frame is
/// rasterised into a 320x240 BGRA buffer for the renderer.
/// </summary>
public static class VideoBackground
{
    private static readonly string[] Extensions =
    {
        ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v",
    };

    public static bool IsVideo(string? path)
        => !string.IsNullOrEmpty(path)
           && Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static IBackgroundFrameProvider? Create(string path)
    {
        if (!IsVideo(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new WpfVideoFrameProvider(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            Log.Write($"video load failed: {path}: {ex.Message}");
            return null;
        }
    }

    private sealed class WpfVideoFrameProvider : IBackgroundFrameProvider, IDisposable
    {
        private const int Width = 320;
        private const int Height = 240;

        private readonly Dispatcher _dispatcher;
        private readonly MediaPlayer _player;
        private readonly byte[] _pixels = new byte[Width * Height * 4];
        private bool _ready;
        private bool _failed;
        private bool _disposed;

        public WpfVideoFrameProvider(string path)
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            // MediaPlayer is a DispatcherObject: it must be created on the UI thread.
            _player = _dispatcher.Invoke(() =>
            {
                var player = new MediaPlayer { IsMuted = true, ScrubbingEnabled = true };
                player.MediaOpened += (_, _) => _ready = true;
                player.MediaFailed += (_, e) =>
                {
                    _failed = true;
                    Log.Write($"video failed: {path}: {e.ErrorException?.Message}");
                };
                player.MediaEnded += (_, _) =>
                {
                    player.Position = TimeSpan.Zero;
                    player.Play();
                };
                player.Open(new Uri(path));
                player.Play();
                return player;
            });
        }

        public bool TryGetFrame(TimeSpan elapsed, out BgraImage frame)
        {
            frame = null!;
            if (_failed || _disposed)
            {
                return false;
            }

            byte[]? pixels = null;
            _dispatcher.Invoke(() => pixels = RenderFrame());
            if (pixels is null)
            {
                return false;
            }

            frame = new BgraImage(Width, Height, pixels);
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _dispatcher.Invoke(() =>
                {
                    _player.Stop();
                    _player.Close();
                });
            }
            catch (InvalidOperationException)
            {
            }
        }

        private byte[]? RenderFrame()
        {
            if (!_ready || _player.NaturalVideoWidth == 0 || _player.NaturalVideoHeight == 0)
            {
                return null;
            }

            // Letterbox to keep the source aspect ratio inside the 320x240 canvas.
            double scale = Math.Min(
                (double)Width / _player.NaturalVideoWidth,
                (double)Height / _player.NaturalVideoHeight);
            double drawWidth = _player.NaturalVideoWidth * scale;
            double drawHeight = _player.NaturalVideoHeight * scale;
            var destination = new Rect(
                (Width - drawWidth) / 2, (Height - drawHeight) / 2, drawWidth, drawHeight);

            var visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, Width, Height));
                context.DrawVideo(_player, destination);
            }

            var target = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.CopyPixels(_pixels, Width * 4, 0);

            return _pixels;
        }
    }
}
