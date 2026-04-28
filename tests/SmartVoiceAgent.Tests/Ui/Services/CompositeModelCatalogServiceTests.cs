using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class CompositeModelCatalogServiceTests
{
    [Fact]
    public async Task GetModelsAsync_EnrichesLiveModelsWithModelsDevMetadata()
    {
        var live = new StubModelCatalogService([
            new ModelCatalogEntry
            {
                Provider = ModelProviderType.OpenAI,
                ProviderId = "openai",
                ModelId = "gpt-5.1-codex",
                DisplayName = "gpt-5.1-codex",
                Source = "provider-live",
                IsAvailable = true
            }
        ]);
        var metadata = new StubModelCatalogService([
            new ModelCatalogEntry
            {
                Provider = ModelProviderType.OpenAI,
                ProviderId = "openai",
                ModelId = "gpt-5.1-codex",
                DisplayName = "GPT-5.1 Codex",
                Source = "models.dev",
                ContextWindow = 400000,
                InputPricePerMillionTokens = 1.25m,
                OutputPricePerMillionTokens = 10m,
                Capabilities = ["reasoning", "tool-calling", "structured-output"]
            }
        ]);
        var service = new CompositeModelCatalogService(live, metadata);

        var models = await service.GetModelsAsync(new ModelProviderProfile
        {
            Provider = ModelProviderType.OpenAI
        });

        models.Should().ContainSingle().Which.Should().Match<ModelCatalogEntry>(model =>
            model.ModelId == "gpt-5.1-codex"
            && model.DisplayName == "GPT-5.1 Codex"
            && model.Source == "provider-live+models.dev"
            && model.IsAvailable
            && model.ContextWindow == 400000
            && model.InputPricePerMillionTokens == 1.25m
            && model.OutputPricePerMillionTokens == 10m
            && model.Capabilities.Contains("reasoning")
            && model.Capabilities.Contains("tool-calling"));
    }

    [Fact]
    public async Task GetModelsAsync_WhenLiveProviderFails_ReturnsModelsDevFallback()
    {
        var live = new FailingModelCatalogService();
        var metadata = new StubModelCatalogService([
            new ModelCatalogEntry
            {
                Provider = ModelProviderType.OpenAI,
                ProviderId = "openai",
                ModelId = "gpt-5.1-codex",
                DisplayName = "GPT-5.1 Codex",
                Source = "models.dev"
            }
        ]);
        var service = new CompositeModelCatalogService(live, metadata);

        var models = await service.GetModelsAsync(new ModelProviderProfile
        {
            Provider = ModelProviderType.OpenAI
        });

        models.Should().ContainSingle().Which.Should().Match<ModelCatalogEntry>(model =>
            model.ModelId == "gpt-5.1-codex"
            && model.Source == "models.dev"
            && !model.IsAvailable);
    }

    private sealed class StubModelCatalogService(IReadOnlyList<ModelCatalogEntry> models) : IModelCatalogService
    {
        public Task<IReadOnlyList<ModelCatalogEntry>> GetModelsAsync(
            ModelProviderProfile profile,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(models);
        }
    }

    private sealed class FailingModelCatalogService : IModelCatalogService
    {
        public Task<IReadOnlyList<ModelCatalogEntry>> GetModelsAsync(
            ModelProviderProfile profile,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Provider unavailable.");
        }
    }
}
