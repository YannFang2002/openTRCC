using OpenTRCC.Core.Rendering;
using Xunit;

namespace OpenTRCC.Core.Tests;

public sealed class FramePumpTests
{
    private static readonly byte[] FrameA = { 1, 2, 3, 4 };
    private static readonly byte[] FrameB = { 1, 2, 3, 5 };

    [Fact]
    public void Sends_First_Frame()
    {
        var pump = new FramePump(TimeSpan.FromSeconds(1));

        Assert.True(pump.ShouldSend(FrameA, DateTimeOffset.Parse("2026-10-24T22:00:00+08:00")));
        Assert.Equal(1, pump.Sent);
    }

    [Fact]
    public void Skips_Identical_Frame_Within_KeepAlive()
    {
        var pump = new FramePump(TimeSpan.FromSeconds(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-24T22:00:00+08:00");
        pump.ShouldSend(FrameA, now);

        Assert.False(pump.ShouldSend(FrameA, now.AddMilliseconds(200)));
        Assert.False(pump.ShouldSend(FrameA, now.AddMilliseconds(900)));
        Assert.Equal(1, pump.Sent);
        Assert.Equal(2, pump.Skipped);
    }

    [Fact]
    public void Resends_Identical_Frame_After_KeepAlive()
    {
        var pump = new FramePump(TimeSpan.FromSeconds(1));
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-24T22:00:00+08:00");
        pump.ShouldSend(FrameA, now);

        Assert.True(pump.ShouldSend(FrameA, now.AddMilliseconds(1600)));
        Assert.Equal(2, pump.Sent);
    }

    [Fact]
    public void Changed_Frame_Is_Always_Sent()
    {
        var pump = new FramePump(TimeSpan.FromSeconds(5));
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-24T22:00:00+08:00");
        pump.ShouldSend(FrameA, now);

        Assert.True(pump.ShouldSend(FrameB, now.AddMilliseconds(50)));
        Assert.Equal(2, pump.Sent);
    }

    [Fact]
    public void Minimum_Interval_Throttles_Changed_Frames()
    {
        var pump = new FramePump(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(40));
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-24T22:00:00+08:00");
        pump.ShouldSend(FrameA, now);

        Assert.False(pump.ShouldSend(FrameB, now.AddMilliseconds(10)));
        Assert.True(pump.ShouldSend(FrameB, now.AddMilliseconds(60)));
    }

    [Fact]
    public void Hash_Detects_Single_Byte_Difference()
    {
        Assert.NotEqual(FramePump.Hash(FrameA), FramePump.Hash(FrameB));
        Assert.Equal(FramePump.Hash(FrameA), FramePump.Hash((byte[])FrameA.Clone()));
    }
}
