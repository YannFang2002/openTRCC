using OpenTRCC.Core.Imaging;

namespace OpenTRCC.Core.Rendering;

/// <summary>
/// Supplies a background frame for the current moment (animated GIF, later video).
/// Implemented outside the renderer so Core stays free of platform decoders.
/// </summary>
public interface IBackgroundFrameProvider
{
    bool TryGetFrame(TimeSpan elapsed, out BgraImage frame);
}
