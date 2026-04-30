using Core.CrossCuttingConcerns.Logging.Serilog;
using FluentAssertions;
using Serilog;
using System.Drawing;
using System.Runtime.Versioning;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

[SupportedOSPlatform("windows6.1")]
public sealed class OcrServiceTests
{
    [Fact]
    public async Task ExtractTextAsync_WhenTessdataIsMissing_ReturnsEmptyResult()
    {
        var service = new OcrService(new TestLogger());
        using var bitmap = new Bitmap(2, 2);

        var lines = await service.ExtractTextAsync(bitmap);

        lines.Should().BeEmpty();
    }

    private sealed class TestLogger : LoggerServiceBase
    {
        public TestLogger()
            : base(new LoggerConfiguration().CreateLogger())
        {
        }
    }
}
