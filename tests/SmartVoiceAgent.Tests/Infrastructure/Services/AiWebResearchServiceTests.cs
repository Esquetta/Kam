using System.Net;
using Core.CrossCuttingConcerns.Logging.Serilog;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Infrastructure.Services.WebResearch;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class AiWebResearchServiceTests
{
    [Fact]
    public async Task SearchAsync_UsesChatCompletionsEndpointForOpenRouterPlans()
    {
        var handler = new RecordingWebResearchHandler();
        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouter:ApiKey"] = "test-openrouter-key",
                ["OpenRouter:Model"] = "openai/gpt-4.1-mini",
                ["WebResearch:SearchApiKey"] = "test-google-key",
                ["WebResearch:SearchEngineId"] = "test-search-engine"
            })
            .Build();
        var service = new AiWebResearchService(httpClient, new TestLogger(), configuration);

        var results = await service.SearchAsync(new WebResearchRequest
        {
            Query = "Kam voice automation",
            Language = "en",
            MaxResults = 2
        });

        results.Should().ContainSingle();
        results[0].Title.Should().Be("Kam release notes");
        handler.ChatCompletionRequests.Should().Be(2);
        handler.GoogleSearchRequests.Should().Be(2);
        handler.LegacyCompletionRequests.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithSingleResult_UsesDirectSearchWithoutOpenRouterPlanning()
    {
        var handler = new RecordingWebResearchHandler();
        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouter:ApiKey"] = "test-openrouter-key",
                ["OpenRouter:Model"] = "openai/gpt-4.1-mini",
                ["WebResearch:SearchApiKey"] = "test-google-key",
                ["WebResearch:SearchEngineId"] = "test-search-engine"
            })
            .Build();
        var service = new AiWebResearchService(httpClient, new TestLogger(), configuration);

        var results = await service.SearchAsync(new WebResearchRequest
        {
            Query = "Kam voice automation",
            Language = "en",
            MaxResults = 1
        });

        results.Should().ContainSingle();
        handler.ChatCompletionRequests.Should().Be(0);
        handler.GoogleSearchRequests.Should().Be(1);
        handler.LegacyCompletionRequests.Should().Be(0);
    }

    private sealed class RecordingWebResearchHandler : HttpMessageHandler
    {
        public int ChatCompletionRequests { get; private set; }

        public int LegacyCompletionRequests { get; private set; }

        public int GoogleSearchRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri?.AbsolutePath.Equals("/api/v1/completions", StringComparison.OrdinalIgnoreCase) == true)
            {
                LegacyCompletionRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("legacy endpoint not available")
                });
            }

            if (request.Method == HttpMethod.Post
                && request.RequestUri?.AbsolutePath.Equals("/api/v1/chat/completions", StringComparison.OrdinalIgnoreCase) == true)
            {
                ChatCompletionRequests++;
                var content = ChatCompletionRequests == 1
                    ? """
                      {"choices":[{"message":{"content":"{\"purpose\":\"smoke\",\"keywords\":[\"Kam voice automation\",\"desktop agent\",\"voice assistant\"],\"preferredSources\":[\"docs\"],\"language\":\"en\"}"}}]}
                      """
                    : """
                      {"choices":[{"message":{"content":"{\"selectedIndices\":[0],\"reasoning\":\"Relevant smoke result\"}"}}]}
                      """;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                });
            }

            if (request.Method == HttpMethod.Get
                && request.RequestUri?.Host.Equals("www.googleapis.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                GoogleSearchRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"items":[{"title":"Kam release notes","link":"https://example.com/kam","snippet":"Production readiness notes."}]}
                        """)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected request: {request.Method} {request.RequestUri}")
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
