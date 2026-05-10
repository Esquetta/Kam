using Core.CrossCuttingConcerns.Logging.Serilog;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Serilog;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Infrastructure.Services;
using SmartVoiceAgent.Infrastructure.Services.Intent;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class AiIntentDetectionServiceTests
{
    [Fact]
    public async Task DetectIntentAsync_WhenAiProviderFails_UsesPatternFallback()
    {
        var service = new AiIntentDetectionService(
            new ThrowingChatClient(),
            new TestLogger(),
            new IntentDetectorService());

        var result = await service.DetectIntentAsync("search google", "en");

        result.Intent.Should().Be(CommandType.SearchWeb);
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new HttpRequestException("provider failed");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return EmptyAsync();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> EmptyAsync()
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class TestLogger : LoggerServiceBase
    {
        public TestLogger()
            : base(new LoggerConfiguration().CreateLogger())
        {
        }
    }
}
