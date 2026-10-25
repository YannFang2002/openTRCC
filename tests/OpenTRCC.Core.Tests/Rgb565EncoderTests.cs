using OpenTRCC.Core.Encoding;
using OpenTRCC.Core.Imaging;
using Xunit;

namespace OpenTRCC.Core.Tests;

public sealed class Rgb565EncoderTests
{
    [Theory]
    [InlineData(255, 0, 0, 0xF800)] // red
    [InlineData(0, 255, 0, 0x07E0)] // green
    [InlineData(0, 0, 255, 0x001F)] // blue
    [InlineData(255, 255, 255, 0xFFFF)] // white
    [InlineData(0, 0, 0, 0x0000)] // black
    public void Pack_Matches_Reference_Values(byte r, byte g, byte b, ushort expected)
    {
        Assert.Equal(expected, Rgb565Encoder.Pack(r, g, b));
    }

    [Fact]
    public void LittleEndian_Writes_Low_Byte_First()
    {
        var image = new BgraImage(1, 1);
        image.SetPixel(0, 0, b: 0, g: 0, r: 255, a: 255);

        byte[] little = Rgb565Encoder.Encode(image, Rgb565ByteOrder.LittleEndian);
        byte[] big = Rgb565Encoder.Encode(image, Rgb565ByteOrder.BigEndian);

        Assert.Equal(new byte[] { 0x00, 0xF8 }, little);
        Assert.Equal(new byte[] { 0xF8, 0x00 }, big);
    }

    [Fact]
    public void Encode_Produces_Two_Bytes_Per_Pixel()
    {
        byte[] payload = Rgb565Encoder.Encode(new BgraImage(320, 240), Rgb565ByteOrder.LittleEndian);

        Assert.Equal(320 * 240 * 2, payload.Length);
    }

    [Fact]
    public void Unpack_Roundtrips_Pack()
    {
        (byte r, byte g, byte b) = Rgb565Encoder.Unpack(Rgb565Encoder.Pack(200, 100, 50));

        Assert.Equal(200, r);
        Assert.Equal(100, g);
        Assert.Equal(48, b); // 5-bit blue
    }
}
