using OpenTRCC.Devices;
using OpenTRCC.Devices.Protocol;
using Xunit;

namespace OpenTRCC.Devices.Tests;

public sealed class ThermalrightType2Tests
{
    [Fact]
    public void SendFrame_Splits_Into_512_Byte_Writes()
    {
        var transport = new RecordingTransport();
        using var device = new ThermalrightType2(transport);
        var frame = new byte[1074];

        device.SendFrame(frame, chunkSize: 512, settleMs: 0);

        Assert.Equal(3, transport.Writes.Count);
        Assert.Equal(512, transport.Writes[0].Length);
        Assert.Equal(512, transport.Writes[1].Length);
        Assert.Equal(50, transport.Writes[2].Length);
        Assert.Equal(1074, transport.TotalBytes);
    }

    [Fact]
    public void SendFrame_With_Zero_Chunk_Writes_The_Whole_Frame()
    {
        var transport = new RecordingTransport();
        using var device = new ThermalrightType2(transport);

        device.SendFrame(new byte[154_112], chunkSize: 0, settleMs: 0);

        Assert.Single(transport.Writes);
        Assert.Equal(154_112, transport.Writes[0].Length);
    }

    [Fact]
    public void Standard_Handshake_Parses_Pm_And_Sub()
    {
        var transport = new RecordingTransport
        {
            NextRead = BuildReply(pm: 0x32, sub: 0x01),
        };

        using var device = new ThermalrightType2(transport);
        HandshakeResult result = device.StandardHandshake();

        Assert.Equal(0x32, result.ProductId);
        Assert.Equal(0x01, result.SubVersion);
        Assert.Single(transport.Writes);
        Assert.Equal(512, transport.Writes[0].Length);
        Assert.True(transport.Writes[0].AsSpan(0, 4).SequenceEqual(new byte[] { 0xDA, 0xDB, 0xDC, 0xDD }));
    }

    [Fact]
    public void Standard_Handshake_Throws_When_No_Reply_Arrives()
    {
        var transport = new RecordingTransport();
        using var device = new ThermalrightType2(transport);

        // Silence surfaces as "reply too short" after the accumulate loop gives up.
        Assert.Throws<LcdDeviceException>(() => device.StandardHandshake());
    }

    private static byte[] BuildReply(int pm, int sub)
    {
        var reply = new byte[512];
        reply[0] = 0xDA;
        reply[1] = 0xDB;
        reply[2] = 0xDC;
        reply[3] = 0xDD;
        reply[4] = (byte)sub;
        reply[5] = (byte)pm;
        reply[12] = 0x01;
        return reply;
    }

    private sealed class RecordingTransport : ILcdTransport
    {
        public List<byte[]> Writes { get; } = new();

        public byte[]? NextRead { get; set; }

        public int TotalBytes => Writes.Sum(w => w.Length);

        public string Description => "recording";

        public void Write(ReadOnlySpan<byte> data) => Writes.Add(data.ToArray());

        public int Read(Span<byte> buffer, TimeSpan timeout)
        {
            if (NextRead is null)
            {
                throw new LcdTimeoutException("no reply in mock");
            }

            int count = Math.Min(buffer.Length, NextRead.Length);
            NextRead.AsSpan(0, count).CopyTo(buffer);
            return count;
        }

        public void Dispose()
        {
        }
    }
}
