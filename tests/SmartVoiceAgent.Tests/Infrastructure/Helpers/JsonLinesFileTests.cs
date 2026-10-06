using System.Text;
using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Helpers;

namespace SmartVoiceAgent.Tests.Infrastructure.Helpers;

public sealed class JsonLinesFileTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kam-jsonl-{Guid.NewGuid():N}.jsonl");

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(64 * 1024)]
    public void ReadLinesNewestFirst_ReturnsLinesInReverseAcrossChunkEdges(int chunkSize)
    {
        File.WriteAllText(_path, "{\"a\":\"ilk\"}\r\n\n{\"b\":\"ğüşıöç\"}\n   \n{\"c\":\"son satır\"}\n", new UTF8Encoding(true));

        JsonLinesFile.ReadLinesNewestFirst(_path, chunkSize)
            .Should().Equal("{\"c\":\"son satır\"}", "{\"b\":\"ğüşıöç\"}", "{\"a\":\"ilk\"}");
    }

    [Fact]
    public void ReadLinesNewestFirst_StopsReadingOnceEnoughLinesAreTaken()
    {
        var lines = Enumerable.Range(0, 20_000).Select(index => $"{{\"index\":{index}}}");
        File.WriteAllLines(_path, lines);

        JsonLinesFile.ReadLinesNewestFirst(_path).Take(2)
            .Should().Equal("{\"index\":19999}", "{\"index\":19998}");
    }

    [Fact]
    public void ReadLinesNewestFirst_EmptyFile_ReturnsNothing()
    {
        File.WriteAllText(_path, string.Empty);

        JsonLinesFile.ReadLinesNewestFirst(_path).Should().BeEmpty();
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
