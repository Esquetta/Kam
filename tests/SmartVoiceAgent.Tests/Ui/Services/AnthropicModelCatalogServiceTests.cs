using System.Net;
using System.Net.Http;
using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class AnthropicModelCatalogServiceTests
{
    [Fact]
    public async Task GetModelsAsync_UsesAnthropicModelsEndpointAndApiKeyHeader()
    {
        using var handler = new StubHttpMessageHandler("""
            {
              "data": [
                { "id": "claude-sonnet-4-6", "display_name": "Claude Sonnet 4.6" },
                { "id": "claude-opus-4-7", "display_name": "Claude Opus 4.7" }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var service = new AnthropicModelCatalogService(httpClient);

        var models = await service.GetModelsAsync(new ModelProviderProfile
        {
            Provider = ModelProviderType.Anthropic,
            Endpoint = "https://api.anthropic.com",
            ApiKey = "sk-ant-test"
        });

        handler.RequestUri.Should().Be(new Uri("https://api.anthropic.com/v1/models"));
        handler.ApiKeyHeader.Should().Be("sk-ant-test");
        handler.AnthropicVersionHeader.Should().Be("2023-06-01");
        models.Should().ContainSingle(model =>
            model.ModelId == "claude-sonnet-4-6"
            && model.DisplayName == "Claude Sonnet 4.6"
            && model.Provider == ModelProviderType.Anthropic
            && model.ProviderId == "anthropic"
            && model.Source == "provider-live"
            && model.IsAvailable);
    }

    [Fact]
    public async Task GetModelsAsync_WhenEndpointAlreadyIncludesV1_DoesNotDuplicatePath()
    {
        using var handler = new StubHttpMessageHandler("""{"data":[]}""");
        using var httpClient = new HttpClient(handler);
        var service = new AnthropicModelCatalogService(httpClient);

        await service.GetModelsAsync(new ModelProviderProfile
        {
            Provider = ModelProviderType.Anthropic,
            Endpoint = "https://api.anthropic.com/v1",
            ApiKey = "sk-ant-test"
        });

        handler.RequestUri.Should().Be(new Uri("https://api.anthropic.com/v1/models"));
    }

    private sealed class StubHttpMessageHandler(string responseContent) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? ApiKeyHeader { get; private set; }

        public string? AnthropicVersionHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKeyHeader = request.Headers.TryGetValues("x-api-key", out var apiKeys)
                ? apiKeys.SingleOrDefault()
                : null;
            AnthropicVersionHeader = request.Headers.TryGetValues("anthropic-version", out var versions)
                ? versions.SingleOrDefault()
                : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            });
        }
    }
}
