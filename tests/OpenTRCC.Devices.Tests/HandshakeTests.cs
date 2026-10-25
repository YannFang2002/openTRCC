using OpenTRCC.Devices.Protocol;
using Xunit;

namespace OpenTRCC.Devices.Tests;

public sealed class HandshakeTests
{
    [Fact]
    public void Request_Is_512_Bytes_With_Magic_And_Init_Marker()
    {
        byte[] request = Handshake.BuildRequest();

        Assert.Equal(512, request.Length);
        Assert.Equal(new byte[] { 0xDA, 0xDB, 0xDC, 0xDD }, request[..4]);
        Assert.Equal(0x01, request[12]);

        // Everything except bytes 0..3 and 12 must be zero.
        for (int i = 4; i < request.Length; i++)
        {
            if (i != 12)
            {
                Assert.Equal(0, request[i]);
            }
        }
    }

    [Fact]
    public void Parse_Reads_Pm_Sub_And_Serial()
    {
        byte[] reply = BuildReply(pm: 0x32, sub: 0x01, status: 0x01, serial: "ABC123");

        HandshakeResult result = Handshake.Parse(reply);

        Assert.Equal(0x32, result.ProductId);
        Assert.Equal(0x01, result.SubVersion);
        Assert.Equal("ABC123", result.SerialNumber);
        Assert.True(result.IsStandard);
    }

    [Fact]
    public void Parse_Rejects_Wrong_Magic()
    {
        byte[] reply = BuildReply(pm: 1, sub: 2, status: 1);
        reply[0] = 0x00;

        Assert.Throws<LcdDeviceException>(() => Handshake.Parse(reply));
    }

    [Fact]
    public void Parse_Rejects_Status_Other_Than_One()
    {
        byte[] reply = BuildReply(pm: 1, sub: 2, status: 0x0E);

        Assert.Throws<LcdDeviceException>(() => Handshake.Parse(reply));
    }

    [Fact]
    public void Parse_Rejects_Short_Reply()
    {
        Assert.Throws<LcdDeviceException>(() => Handshake.Parse(new byte[8]));
    }

    [Fact]
    public void Parse_Tolerates_Missing_Serial_Area()
    {
        HandshakeResult result = Handshake.Parse(BuildReply(pm: 0x31, sub: 2, status: 1)[..16]);

        Assert.Null(result.SerialNumber);
        Assert.Equal(0x31, result.ProductId);
    }

    [Fact]
    public void ParseShort_Accepts_The_Streaming_Reply()
    {
        // Real capture shape: magic + SUB + PM, only 8 bytes long.
        var reply = new byte[] { 0xDA, 0xDB, 0xDC, 0xDD, 0x00, 0x35, 0x00, 0x00 };

        HandshakeResult result = Handshake.ParseShort(reply);

        Assert.Equal(0x35, result.ProductId);
        Assert.Equal(0, result.SubVersion);
        Assert.Equal(HandshakeMode.Streamed, result.Mode);
        Assert.False(result.IsStandard);
    }

    [Fact]
    public void ParseShort_Rejects_Short_Garbage()
    {
        Assert.Throws<LcdDeviceException>(() => Handshake.ParseShort(new byte[] { 1, 2, 3, 4, 5, 6 }));
        Assert.Throws<LcdDeviceException>(() => Handshake.ParseShort(new byte[5]));
    }

    [Fact]
    public void Annotate_Lists_Key_Offsets()
    {
        string text = Handshake.Annotate(BuildReply(pm: 0x32, sub: 1, status: 1, serial: "SN42"));

        Assert.Contains("SUB", text);
        Assert.Contains("PM", text);
        Assert.Contains("SN42", text);
    }

    private static byte[] BuildReply(int pm, int sub, int status, string? serial = null)
    {
        var reply = new byte[512];
        reply[0] = 0xDA;
        reply[1] = 0xDB;
        reply[2] = 0xDC;
        reply[3] = 0xDD;
        reply[4] = (byte)sub;
        reply[5] = (byte)pm;
        reply[12] = (byte)status;

        if (serial is not null)
        {
            System.Text.Encoding.ASCII.GetBytes(serial).CopyTo(reply, 20);
        }

        return reply;
    }
}
