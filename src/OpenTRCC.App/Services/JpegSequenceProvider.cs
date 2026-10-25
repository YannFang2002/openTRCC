using System;
using System.IO;
using System.Text.Json;
using OpenTRCC.Core.Imaging;
using OpenTRCC.Core.Rendering;
using SkiaSharp;

namespace OpenTRCC.App.Services;

/// <summary>
/// Plays a pre-extracted JPEG frame sequence. Decoding one 320x240 JPEG costs a
/// couple of milliseconds, so the render loop stays cheap even with video
/// backgrounds.
/// </summary>
public sealed class JpegSequenceProvider : IBackgroundFrameProvider, IDisposable
{
    private readonly string _directory;
    private readonly int _fps;
    private readonly int _count;
    private int _lastIndex = -1;
    private BgraImage? _last;

    public JpegSequenceProvider(string metaPath)
    {
        _directory = Path.GetDirectoryName(metaPath)
            ?? throw new ArgumentException("meta.json has no directory", nameof(metaPath));

        var meta = JsonSerializer.Deserialize<VideoFrameExtractor.SequenceMeta>(
            File.ReadAllText(metaPath))
            ?? throw new InvalidDataException("frame sequence meta.json is empty");

        _fps = Math.Max(1, meta.Fps);
        _count = Math.Max(1, meta.Count);
    }

    public bool TryGetFrame(TimeSpan elapsed, out BgraImage frame)
    {
        frame = null!;
        if (_count == 0)
        {
            return false;
        }

        int index = (int)(elapsed.TotalSeconds * _fps) % _count;
        if (index < 0)
        {
            index += _count;
        }

        if (_last is not null && index == _lastIndex)
        {
            frame = _last;
            return true;
        }

        string path = Path.Combine(_directory, $"frame-{index:D5}.jpg");
        if (!File.Exists(path))
        {
            return false;
        }

        using SKBitmap? bitmap = SKBitmap.Decode(path);
        if (bitmap is null)
        {
            return false;
        }

        _last = TestPatternRenderer.ToBgra(bitmap);
        _lastIndex = index;
        frame = _last;
        return true;
    }

    public void Dispose()
    {
        _last = null;
        _lastIndex = -1;
    }
}
