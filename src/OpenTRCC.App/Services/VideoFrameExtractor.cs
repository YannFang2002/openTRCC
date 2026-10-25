using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenTRCC.Core.Imaging;
using OpenTRCC.Core.Rendering;
using SkiaSharp;

namespace OpenTRCC.App.Services;

/// <summary>
/// Converts a video into a folder of JPEG frames (and meta.json) once, at import
/// time. Frames keep the source aspect ratio, so the theme's fit mode decides
/// whether they are cropped, fitted or stretched — no baked-in letterbox.
/// </summary>
public static class VideoFrameExtractor
{
    public const int Width = 320;
    public const int Height = 240;
    public const int DefaultFps = 20;
    public const int DefaultMaxSeconds = 30;

    public sealed record SequenceMeta(int Fps, int Width, int Height, int Count, string Source);

    /// <summary>Extracts frames; returns the path of the generated meta.json.</summary>
    public static Task<string> ExtractAsync(
        string videoPath,
        string themeDirectory,
        IProgress<double>? progress = null,
        int fps = DefaultFps,
        int maxSeconds = DefaultMaxSeconds)
        => Task.Run(() => Extract(videoPath, themeDirectory, progress, fps, maxSeconds));

    public static string Extract(
        string videoPath,
        string themeDirectory,
        IProgress<double>? progress = null,
        int fps = DefaultFps,
        int maxSeconds = DefaultMaxSeconds)
    {
        string name = Path.GetFileNameWithoutExtension(videoPath);
        string directory = Path.Combine(themeDirectory, "assets", name + ".frames");
        Directory.CreateDirectory(directory);

        string metaPath = Path.Combine(directory, "meta.json");
        Exception? failure = null;
        int written = 0;

        // A dedicated STA thread owns the MediaPlayer; a DispatcherTimer drives the
        // capture at the target fps while the dispatcher pump decodes frames.
        var thread = new Thread(() =>
        {
            MediaPlayer? player = null;
            Dispatcher? dispatcher = null;

            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                player = new MediaPlayer { IsMuted = true, ScrubbingEnabled = true };

                var completed = new ManualResetEventSlim(false);
                int frameCount = 0;
                int index = 0;

                player.MediaOpened += (_, _) =>
                {
                    player.Play();
                    TimeSpan duration = player.NaturalDuration.HasTimeSpan
                        ? player.NaturalDuration.TimeSpan
                        : TimeSpan.FromSeconds(maxSeconds);

                    double seconds = Math.Min(duration.TotalSeconds, maxSeconds);
                    frameCount = Math.Max(1, (int)Math.Round(seconds * fps));

                    // Preserve the source aspect ratio: a 16:9 clip becomes 320x180,
                    // so cover/contain/stretch have a visible effect in the editor.
                    double fit = Math.Min(
                        (double)Width / player.NaturalVideoWidth,
                        (double)Height / player.NaturalVideoHeight);
                    int frameWidth = Math.Max(2, (int)Math.Round(player.NaturalVideoWidth * fit));
                    int frameHeight = Math.Max(2, (int)Math.Round(player.NaturalVideoHeight * fit));

                    var timer = new DispatcherTimer(
                        TimeSpan.FromMilliseconds(1000.0 / fps),
                        DispatcherPriority.Render,
                        (_, _) =>
                        {
                            try
                            {
                                byte[] pixels = Capture(player, frameWidth, frameHeight);
                                WriteJpeg(
                                    Path.Combine(directory, $"frame-{index:D5}.jpg"),
                                    pixels,
                                    frameWidth,
                                    frameHeight);
                                index++;
                                written = index;
                                progress?.Report(frameCount == 0 ? 0 : (double)index / frameCount);

                                if (index < frameCount)
                                {
                                    return;
                                }

                                var meta = new SequenceMeta(
                                    fps, frameWidth, frameHeight, index, Path.GetFileName(videoPath));
                                File.WriteAllText(metaPath, JsonSerializer.Serialize(meta));
                                player.Close();
                                completed.Set();
                                dispatcher.InvokeShutdown();
                            }
                            catch (Exception ex)
                            {
                                failure = ex;
                                completed.Set();
                                dispatcher.InvokeShutdown();
                            }
                        },
                        dispatcher);

                    timer.Start();
                };

                player.MediaFailed += (_, e) =>
                {
                    failure = e.ErrorException ?? new InvalidOperationException("media failed");
                    completed.Set();
                    dispatcher.InvokeShutdown();
                };

                player.Open(new Uri(videoPath));
                Dispatcher.Run();
                completed.Wait(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                failure = ex;
                dispatcher?.InvokeShutdown();
            }
            finally
            {
                player?.Close();
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException($"视频转码失败：{failure.Message}", failure);
        }

        if (!File.Exists(metaPath))
        {
            throw new InvalidOperationException("视频转码没有产生任何帧（文件可能没有视频轨）。");
        }

        Log.Write($"video extracted: {videoPath} -> {metaPath} ({written} frames)");
        return metaPath;
    }

    private static byte[] Capture(MediaPlayer player, int width, int height)
    {
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            context.DrawVideo(player, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var pixels = new byte[width * height * 4];
        target.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static void WriteJpeg(string path, byte[] bgra, int width, int height)
    {
        using SKBitmap bitmap = TestPatternRenderer.FromBgra(new BgraImage(width, height, bgra));
        File.WriteAllBytes(path, TestPatternRenderer.EncodeJpeg(bitmap, 85));
    }
}
