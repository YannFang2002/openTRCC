using OpenTRCC.Core.Encoding;
using Xunit;

namespace OpenTRCC.Core.Tests;

public sealed class FrameChunkerTests
{
    [Fact]
    public void Target_Frame_Splits_Into_301_Equal_Chunks()
    {
        const int frameLength = 154_112;
        IReadOnlyList<FrameChunker.Chunk> chunks = FrameChunker.Split(frameLength, 512);

        Assert.Equal(301, chunks.Count);
        Assert.All(chunks, chunk => Assert.Equal(512, chunk.Length));
        Assert.Equal(0, chunks[0].Offset);
        Assert.Equal(frameLength - 512, chunks[^1].Offset);
    }

    [Fact]
    public void Last_Chunk_May_Be_Short_For_Larger_Transfers()
    {
        IReadOnlyList<FrameChunker.Chunk> chunks = FrameChunker.Split(154_112, 4096);

        Assert.Equal(38, chunks.Count);
        Assert.Equal(4096, chunks[0].Length);
        Assert.Equal(154_112 - (37 * 4096), chunks[^1].Length);
    }

    [Fact]
    public void Rejects_Zero_Chunk()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameChunker.Split(1024, 0));
    }
}
