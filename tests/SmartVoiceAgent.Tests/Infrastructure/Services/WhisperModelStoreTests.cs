using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class WhisperModelStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"kam-models-{Guid.NewGuid():N}");

    [Fact]
    public async Task EnsureModelAsync_DownloadsOnceAndReportsProgress()
    {
        var downloads = 0;
        using var store = new WhisperModelStore(NullLogger<WhisperModelStore>.Instance, _directory, (info, _) =>
        {
            downloads++;
            return Task.FromResult<Stream>(new MemoryStream(new byte[200_000]));
        });
        var reports = new List<double>();

        store.IsDownloaded("base").Should().BeFalse();
        var path = await store.EnsureModelAsync("base", new InlineProgress(reports.Add));
        await store.EnsureModelAsync("base");

        path.Should().Be(Path.Combine(_directory, "ggml-base.bin"));
        File.Exists(path).Should().BeTrue();
        store.IsDownloaded("base").Should().BeTrue();
        downloads.Should().Be(1);
        reports.Should().NotBeEmpty().And.EndWith(1);
        Directory.GetFiles(_directory, "*.download").Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureModelAsync_LeavesNoPartialFileWhenTheDownloadFails()
    {
        using var store = new WhisperModelStore(NullLogger<WhisperModelStore>.Instance, _directory, (_, _) =>
            Task.FromResult<Stream>(new FailingStream()));

        var act = () => store.EnsureModelAsync("tiny");

        await act.Should().ThrowAsync<IOException>();
        store.IsDownloaded("tiny").Should().BeFalse();
        Directory.GetFiles(_directory).Should().BeEmpty();
    }

    [Theory]
    [InlineData("small", "small")]
    [InlineData("LARGE-V3-TURBO", "large-v3-turbo")]
    [InlineData("medium", "base")]
    [InlineData(null, "base")]
    public void Catalog_NormalizesModelNames(string? model, string expected)
    {
        SpeechModelCatalog.Normalize(model).Should().Be(expected);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class FailingStream : MemoryStream
    {
        private bool _sent;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent)
            {
                throw new IOException("Connection reset");
            }

            _sent = true;
            buffer.Span[..10].Clear();
            return ValueTask.FromResult(10);
        }
    }
}
