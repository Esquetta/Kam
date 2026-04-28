using System.Net;
using System.Net.Http;
using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class OpenAiCompatibleModelCatalogServiceTests
{
    [Fact]
    public async Task GetModelsAsync_UsesModelsEndpointAndReturnsTextGenerationModels()
    {
        using var handler = new StubHttpMessageHandler("""{"object":"list","data":[{"id":"gpt-5.2","owned_by":"openai"},{"id":"text-embedding-3-small","owned_by":"openai"},{"id":"gpt-4.1-mini","owned_by":"openai"}]}""");
        using var httpClient = new HttpClient(handler);
        var service = new OpenAiCompatibleModelCatalogService(httpClient);

        var models = await service.GetModelsAsync(new ModelProviderProfile
        {
            Provider = ModelProviderType.OpenAI,
            Endpoint = "https://api.openai.com/v1",
            ApiKey = "sk-test"
        });

        handler.RequestUri.Should().Be(new Uri("https://api.openai.com/v1/models"));
        handler.AuthorizationHeader.Should().Be("Bearer sk-test");
        models.Select(model => model.ModelId).Should().Equal("gpt-5.2", "gpt-4.1-mini");
        models.Should().OnlyContain(model =>
            model.Provider == ModelProviderType.OpenAI
            && model.Source == "provider-live"
            && model.IsAvailable);
    }

    [Fact]
    public async Task GetModelsAsync_OpenRouter_AddsTextFilterAndReadsMetadata()
    {
        using var handler = new StubHttpMessageHandler("""
            {
              "data": [
                {
                  "id": "openai/gpt-5.1-codex",
                  "name": "GPT-5.1 Codex",
                  "context_length": 400000,
                  "pricing": {
                    "prompt": "0.00000125",
                    "completion": "0.000010"
                  },
                  "architecture": {
                    "input_modalities": ["text", "image"],
                    "output_modalities": ["text"]
                  },
                  "supported_parameters": ["tools", "response_format"]
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var service = new OpenAiCompatibleModelCatalogService(httpClient);

        var models = await service.GetModelsAsync(new ModelProviderProfile
        {
            Provider = ModelProviderType.OpenRouter,
            Endpoint = "https://openrouter.ai/api/v1",
            ApiKey = "sk-or-test"
        });

        handler.RequestUri.Should().Be(new Uri("https://openrouter.ai/api/v1/models?output_modalities=text"));
        handler.AuthorizationHeader.Should().Be("Bearer sk-or-test");
        models.Should().ContainSingle().Which.Should().Match<ModelCatalogEntry>(model =>
            model.ModelId == "openai/gpt-5.1-codex"
            && model.DisplayName == "GPT-5.1 Codex"
            && model.ContextWindow == 400000
            && model.InputPricePerMillionTokens == 1.25m
            && model.OutputPricePerMillionTokens == 10m
            && model.Capabilities.Contains("text-input")
            && model.Capabilities.Contains("image-input")
            && model.Capabilities.Contains("text-output")
            && model.Capabilities.Contains("tool-calling")
            && model.Capabilities.Contains("structured-output"));
    }

    private sealed class StubHttpMessageHandler(string responseContent) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? AuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationHeader = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            });
        }
    }
}
