using OpenTRCC.Sensors.PresentMon;
using Xunit;

namespace OpenTRCC.Sensors.Tests;

public sealed class FpsMathTests
{
    [Fact]
    public void PercentileFps_Uses_Slowest_Frames_For_Low()
    {
        // Four fast frames and one slow one: the 1% low is defined by the slow frame.
        double[] frameTimes = { 5, 5, 5, 5, 100 };

        Assert.Equal(10, FpsMath.PercentileFps(frameTimes, 0.99), 3);
    }

    [Fact]
    public void PercentileFps_Of_Uniform_Frames_Is_Reciprocal()
    {
        double[] frameTimes = { 6.944, 6.944, 6.944 };

        Assert.Equal(144, FpsMath.PercentileFps(frameTimes, 0.99), 0);
    }

    [Fact]
    public void PercentileFps_Ignores_Non_Positive_Samples()
    {
        double[] frameTimes = { 0, 0, 8 };

        Assert.Equal(125, FpsMath.PercentileFps(frameTimes, 0.99), 0);
    }

    [Fact]
    public void PercentileFps_Returns_NaN_When_Nothing_Usable()
    {
        Assert.True(double.IsNaN(FpsMath.PercentileFps(Array.Empty<double>())));
        Assert.True(double.IsNaN(FpsMath.PercentileFps(new double[] { 0, 0 })));
    }

    [Fact]
    public void Smooth_Seeds_On_First_Sample()
    {
        Assert.Equal(180, FpsMath.Smooth(0, 180));
    }

    [Fact]
    public void Smooth_Damps_A_Single_Spike()
    {
        double smoothed = FpsMath.Smooth(180, 220, alpha: 0.3);

        Assert.Equal(192, smoothed, 3);
        Assert.True(smoothed < 220);
    }

    [Fact]
    public void Smooth_Reaches_Steady_State()
    {
        double value = FpsMath.Smooth(0, 100);
        for (int i = 0; i < 30; i++)
        {
            value = FpsMath.Smooth(value, 200, alpha: 0.5);
        }

        Assert.Equal(200, value, 1);
    }
}
