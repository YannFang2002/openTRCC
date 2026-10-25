using OpenTRCC.Core.Encoding;
using OpenTRCC.Devices;
using Xunit;

namespace OpenTRCC.Devices.Tests;

public sealed class DeviceCatalogTests
{
    [Theory]
    [InlineData(49)]
    [InlineData(50)]
    [InlineData(51)]
    [InlineData(52)]
    [InlineData(53)]
    [InlineData(58)]
    public void Target_Panel_Pms_Map_To_240x320_Line_Order(int pm)
    {
        Assert.True(DeviceCatalog.TryResolve(pm, out DeviceProfile profile));
        Assert.Equal(320, profile.CanvasWidth);
        Assert.Equal(240, profile.CanvasHeight);
        Assert.Equal(240, profile.LineWidth);
        Assert.Equal(320, profile.LineHeight);
        Assert.Equal(90, profile.LineRotation);
        Assert.Equal(FrameEncoding.Rgb565, profile.Encoding);
    }

    [Fact]
    public void Pm49_Is_Big_Endian_Others_Are_Little()
    {
        Assert.True(DeviceCatalog.TryResolve(49, out DeviceProfile big));
        Assert.True(DeviceCatalog.TryResolve(50, out DeviceProfile little));

        Assert.Equal(Rgb565ByteOrder.BigEndian, big.ByteOrder);
        Assert.Equal(Rgb565ByteOrder.LittleEndian, little.ByteOrder);
    }

    [Fact]
    public void Unknown_Pm_Is_Reported()
    {
        Assert.False(DeviceCatalog.TryResolve(999, out _));
    }

    [Fact]
    public void ResolveOrDefault_Keeps_Sub_Version_For_Unknown_Pm()
    {
        DeviceProfile profile = DeviceCatalog.ResolveOrDefault(999, 7);

        Assert.Equal(999, profile.ProductId);
        Assert.Equal(7, profile.SubVersion);
        Assert.Equal(320, profile.CanvasWidth);
    }

    [Fact]
    public void Jpeg_Panels_Are_Registered()
    {
        Assert.True(DeviceCatalog.TryResolve(54, out DeviceProfile profile));
        Assert.Equal(FrameEncoding.Jpeg, profile.Encoding);
    }
}
