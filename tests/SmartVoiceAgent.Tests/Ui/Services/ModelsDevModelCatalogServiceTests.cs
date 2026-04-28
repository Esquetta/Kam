using System.Net;
using System.Net.Http;
using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class ModelsDevModelCatalogServiceTests
{
    [Fact]
    public async Task GetModelsAsync_OpenAi_ReturnsModelsWithCapabilitiesAndPricing()
    {
        using var handler = new StubHttpMessageHandler("""
            {
              "openai": {
                "id": "openai",
                "name": "OpenAI",
                "models": {
                  "gpt-5.1-codex": {
                    "id": "gpt-5.1-codex",
                    "name": "GPT-5.1 Codex",
                    "reasoning": true,
                    "tool_call": true,
                    "structured_output": true,
                    "modalities": {
                      "input": ["text", "image"],
                      "output": ["text"]
                    },
                    "cost": {
                      "input": 1.25,
                      "output": 10
                    },
                    "limit": {
                      "context": 400000,
                      "output": 128000
                    }
                  }
                }
              }
            }
            """);
        using var httpClient = new HttpClient(handler);
        var service = new ModelsDevModelCatalogService(httpClient);

        var models = await service.GetModelsAsync(new ModelProviderProfile
        {
            Provider = ModelProviderType.OpenAI
        });

        handler.RequestUri.Should().Be(new Uri("https://models.dev/api.json"));
        models.Should().ContainSingle().Which.Should().Match<ModelCatalogEntry>(model =>
            model.Provider == ModelProviderType.OpenAI
            && model.ProviderId == "openai"
            && model.ModelId == "gpt-5.1-codex"
            && model.DisplayName == "GPT-5.1 Codex"
            && model.Source == "models.dev"
            && !model.IsAvailable
            && model.ContextWindow == 400000
            && model.MaxOutputTokens == 128000
            && model.InputPricePerMillionTokens == 1.25m
            && model.OutputPricePerMillionTokens == 10m
            && model.Capabilities.Contains("reasoning")
            && model.Capabilities.Contains("tool-calling")
            && model.Capabilities.Contains("structured-output"));
    }

    [Fact]
    public async Task GetModelsAsync_OpenRouter_UsesProviderQualifiedModelIds()
    {
        using var handler = new StubHttpMessageHandler("""
            {
              "openai": {
                "id": "openai",
                "name": "OpenAI",
                "models": {
                  "gpt-5.1-codex": {
                    "id": "gpt-5.1-codex",
                    "name": "GPT-5.1 Codex",
                    "tool_call": true,
                    "modalities": {
                      "input": ["text"],
                      "output": ["text"]
                    }
                  }
                }
              }
            }
            """);
        using var httpClient = new HttpClient(handler);
        var service = new ModelsDevModelCatalogService(httpClient);

        var models = await service.GetModelsAsync(new ModelProviderProfile
        {
            Provider = ModelProviderType.OpenRouter
        });

        models.Should().ContainSingle().Which.ModelId.Should().Be("openai/gpt-5.1-codex");
    }

    private sealed class StubHttpMessageHandler(string responseContent) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            });
        }
    }
}
