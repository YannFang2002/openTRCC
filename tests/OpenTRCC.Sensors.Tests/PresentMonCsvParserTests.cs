using OpenTRCC.Sensors.PresentMon;
using Xunit;

namespace OpenTRCC.Sensors.Tests;

public sealed class PresentMonCsvParserTests
{
    private const string Header =
        "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode," +
        "MsBetweenPresents,MsInPresentAPI,MsBetweenDisplayChange,MsUntilRenderComplete,MsUntilDisplayed," +
        "VidPnSourceId,PresentMode,WasBatched,DwmNotified";

    [Fact]
    public void Reads_Header_And_Frame()
    {
        var parser = new PresentMonCsvParser();

        Assert.True(parser.TryReadHeader(Header));
        Assert.True(parser.TryParse(
            "game.exe,4242,0x000001A2B3C4D5E6,DXGI,1,0,0,Hardware: Independent Flip,16.67,0.2,16.6,0.5,1.2,0,0,0,0",
            out PresentMonFrame frame));

        Assert.Equal("game.exe", frame.Application);
        Assert.Equal(4242, frame.ProcessId);
        Assert.Equal(16.67, frame.MsBetweenPresents, 3);
        Assert.Equal(0, frame.VidPnSourceId);
    }

    [Fact]
    public void Tolerates_Quoted_Application_Names()
    {
        var parser = new PresentMonCsvParser();
        parser.TryReadHeader(Header);

        Assert.True(parser.TryParse(
            "\"My, Game.exe\",7,0x1,DXGI,1,0,0,Hardware,8.33,0.1,8.3,0.2,0.3,1,0,0,0",
            out PresentMonFrame frame));

        Assert.Equal("My, Game.exe", frame.Application);
        Assert.Equal(1, frame.VidPnSourceId);
    }

    [Fact]
    public void Rejects_Garbage_And_Short_Rows()
    {
        var parser = new PresentMonCsvParser();
        Assert.False(parser.TryParse("game.exe,4242", out _));

        parser.TryReadHeader(Header);
        Assert.False(parser.TryParse("game.exe,not-a-number,0x1", out _));
        Assert.False(parser.TryParse("", out _));
    }

    [Fact]
    public void Header_Without_Required_Columns_Is_Rejected()
    {
        var parser = new PresentMonCsvParser();

        Assert.False(parser.TryReadHeader("Application,SwapChainAddress"));
        Assert.False(parser.HasHeader);
    }
}
