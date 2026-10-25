using OpenTRCC.Core.Encoding;
using Xunit;

namespace OpenTRCC.Core.Tests;

public sealed class FrameAssemblerTests
{
    private const int PayloadLength = 240 * 320 * 2;

    [Fact]
    public void Header_Matches_Type2_Layout_For_320x240_Panel()
    {
        byte[] header = FrameAssembler.BuildHeader(240, 320, FrameEncoding.Rgb565, PayloadLength);

        Assert.Equal(20, header.Length);
        Assert.Equal(new byte[] { 0xDA, 0xDB, 0xDC, 0xDD }, header[..4]);
        Assert.Equal(new byte[] { 0x02, 0x00 }, header[4..6]);
        Assert.Equal(new byte[] { 0x01, 0x00 }, header[6..8]);
        Assert.Equal(new byte[] { 0xF0, 0x00 }, header[8..10]); // width 240 LE
        Assert.Equal(new byte[] { 0x40, 0x01 }, header[10..12]); // height 320 LE
        Assert.Equal(0x02, header[12]);
        Assert.Equal(new byte[] { 0x00, 0x58, 0x02, 0x00 }, header[16..20]); // 153600 LE
    }

    [Fact]
    public void Jpeg_Header_Uses_Zero_Marker_And_Real_Size()
    {
        byte[] header = FrameAssembler.BuildHeader(240, 320, FrameEncoding.Jpeg, 12_345);

        Assert.Equal(new byte[] { 0x00, 0x00 }, header[6..8]);
        Assert.Equal(12_345u, BitConverter.ToUInt32(header, 16));
    }

    [Fact]
    public void Assembled_Frame_Is_301_Chunks_Of_512_Bytes()
    {
        var payload = new byte[PayloadLength];
        Array.Fill(payload, (byte)0xFF);

        byte[] frame = FrameAssembler.Assemble(payload, 240, 320, FrameEncoding.Rgb565, 512);

        Assert.Equal(154_112, frame.Length);
        Assert.Equal(301, FrameChunker.Count(frame.Length, 512));
        Assert.Equal(301, FrameChunker.Split(frame.Length, 512).Count);

        // Payload must be byte-identical right after the header.
        Assert.Equal(payload, frame[20..(20 + PayloadLength)]);

        // Tail padding is zero.
        for (int i = 20 + PayloadLength; i < frame.Length; i++)
        {
            Assert.Equal(0, frame[i]);
        }
    }

    [Fact]
    public void RoundUp_Handles_Already_Aligned_Values()
    {
        Assert.Equal(512, FrameAssembler.RoundUp(512, 512));
        Assert.Equal(1024, FrameAssembler.RoundUp(513, 512));
    }
}
