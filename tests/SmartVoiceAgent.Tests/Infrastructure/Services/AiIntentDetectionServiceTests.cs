using System.Net;
using Core.CrossCuttingConcerns.Logging.Serilog;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
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
        using var httpClient = new HttpClient(new StaticStatusHandler(HttpStatusCode.InternalServerError));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouter:ApiKey"] = "test-key",
                ["OpenRouter:Model"] = "test-model"
            })
            .Build();

        var service = new AiIntentDetectionService(
            httpClient,
            new TestLogger(),
            configuration,
            new IntentDetectorService());

        var result = await service.DetectIntentAsync("search google", "en");

        result.Intent.Should().Be(CommandType.SearchWeb);
    }

    private sealed class StaticStatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("provider failed")
            });
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
