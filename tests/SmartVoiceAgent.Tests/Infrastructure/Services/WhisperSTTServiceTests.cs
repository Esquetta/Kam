using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class WhisperSTTServiceTests
{
    [Fact]
    public async Task Constructor_DoesNotLoadModelUntilFirstTranscription()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Whisper:ModelPath"] = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.bin")
            })
            .Build();

        using var service = new WhisperSTTService(NullLogger<WhisperSTTService>.Instance, configuration);

        service.IsModelLoaded.Should().BeFalse();

        var result = await service.ConvertToTextAsync(new byte[48]);

        result.Text.Should().BeEmpty();
        result.ErrorMessage.Should().NotBeEmpty("the missing model is reported when a transcription needs it");
        service.IsModelLoaded.Should().BeFalse();
    }
}
