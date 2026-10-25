using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTRCC.Sensors.PresentMon;

/// <summary>Frame-time to FPS maths shared by the PresentMon source and its tests.</summary>
public static class FpsMath
{
    /// <summary>
    /// FPS at the given percentile of frame times (0.99 → 1% low): frame times are
    /// sorted ascending and 1000 / P&lt;percentile&gt; is returned. Non-positive
    /// samples are ignored (they mean "no timing available", not a 0 ms frame).
    /// </summary>
    public static double PercentileFps(IEnumerable<double> frameTimesMs, double percentile = 0.99)
    {
        var positive = frameTimesMs.Where(ms => ms > 0).OrderBy(ms => ms).ToArray();
        if (positive.Length == 0)
        {
            return double.NaN;
        }

        int index = Math.Clamp(
            (int)Math.Ceiling(positive.Length * percentile) - 1, 0, positive.Length - 1);

        double frameTime = positive[index];
        return frameTime > 0 ? 1000.0 / frameTime : double.NaN;
    }

    /// <summary>
    /// Exponential moving average used for display: a 180 fps reading that jitters
    /// ±10 every second should read as a steady number, not a blinking one.
    /// </summary>
    public static double Smooth(double previous, double current, double alpha = 0.3)
    {
        if (previous <= 0 || double.IsNaN(previous))
        {
            return current;
        }

        return previous * (1 - alpha) + current * alpha;
    }
}
