using OpenEgg.Discord;
using Xunit;

namespace OpenEgg.Tests;

public sealed class TextChunkerTests
{
    [Fact]
    public void ChunkPreservesShortText()
    {
        Assert.Equal(["hello"], TextChunker.Chunk("hello", 10));
    }

    [Fact]
    public void ChunkSplitsOnLineBoundaries()
    {
        Assert.Equal(["aaa\nbbb", "ccc"], TextChunker.Chunk("aaa\nbbb\nccc", 8));
    }

    [Fact]
    public void ChunkHardSplitsLongLines()
    {
        Assert.Equal(["abcd", "efgh", "ij"], TextChunker.Chunk("abcdefghij", 4));
    }
}
