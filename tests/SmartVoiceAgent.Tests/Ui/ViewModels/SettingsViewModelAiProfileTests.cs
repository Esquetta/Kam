using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System.Reactive.Linq;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public class SettingsViewModelAiProfileTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-settings-viewmodel-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultAiSettings_AreOpenRouterCompatible()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settingsService);

        viewModel.AiProvider.Should().Be("OpenRouter");
        viewModel.AiEndpoint.Should().Be("https://openrouter.ai/api/v1");
        viewModel.ChatProvider.Should().Be("OpenRouter");
        viewModel.ChatEndpoint.Should().Be("https://openrouter.ai/api/v1");
        settingsService.ModelProviderProfiles.Should().ContainSingle(
            p => p.Provider == ModelProviderType.OpenRouter && p.Roles.Contains(ModelProviderRole.Planner));
        settingsService.ModelProviderProfiles.Should().ContainSingle(
            p => p.Provider == ModelProviderType.OpenRouter && p.Roles.Contains(ModelProviderRole.Chat));
    }

    [Fact]
    public async Task TestAiConnectionCommand_InvalidEndpoint_ShowsValidationError()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = CreateViewModel(settingsService);
        viewModel.AiEndpoint = "not-a-url";
        viewModel.AiApiKey = "sk-test";
        viewModel.AiModelId = "openai/gpt-4.1-mini";

        await viewModel.TestAiConnectionCommand.Execute().FirstAsync();

        viewModel.IsAiProfileValid.Should().BeFalse();
        viewModel.AiProfileStatus.Should().Contain("Planner: A valid endpoint is required.");
    }

    [Fact]
    public async Task TestAiConnectionCommand_OllamaWithoutApiKey_IsValid()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = CreateViewModel(settingsService);
        viewModel.AiProvider = "Ollama";
        viewModel.AiEndpoint = "http://localhost:11434/v1";
        viewModel.AiApiKey = string.Empty;
        viewModel.AiModelId = "llama3.1";

        await viewModel.TestAiConnectionCommand.Execute().FirstAsync();

        viewModel.IsAiProfileValid.Should().BeTrue();
        viewModel.AiProfileStatus.Should().Contain("Connection verified");
        settingsService.ModelProviderProfiles.Should().ContainSingle(p =>
            p.Provider == ModelProviderType.Ollama
            && p.Enabled
            && p.ModelId == "llama3.1");
    }

    [Fact]
    public async Task ChatProfileSettings_SaveSeparateActiveChatProfile()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = CreateViewModel(settingsService);
        viewModel.AiApiKey = "sk-planner";
        viewModel.ChatProvider = "OpenAICompatible";
        viewModel.ChatEndpoint = "https://api.example.com/v1";
        viewModel.ChatApiKey = "sk-chat";
        viewModel.ChatModelId = "custom/chat-model";
        viewModel.ActiveChatProfileId = "custom-chat";

        await viewModel.TestAiConnectionCommand.Execute().FirstAsync();

        settingsService.ActiveChatProfileId.Should().Be("custom-chat");
        settingsService.ModelProviderProfiles.Should().ContainSingle(p =>
            p.Id == "custom-chat"
            && p.Roles.Contains(ModelProviderRole.Chat)
            && p.ModelId == "custom/chat-model"
            && p.Enabled);
        settingsService.ModelProviderProfiles.Should().ContainSingle(p =>
            p.Roles.Contains(ModelProviderRole.Planner));
    }

    [Fact]
    public async Task TestAiConnectionCommand_OpenAiProvider_UsesLiveConnectionTestAndShowsSuccess()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        var connectionTestService = new StubModelConnectionTestService(ModelConnectionTestResult.Passed(42));
        using var viewModel = CreateViewModel(settingsService, connectionTestService);
        viewModel.AiProvider = "OpenAI";
        viewModel.AiApiKey = "sk-planner";
        viewModel.AiModelId = "gpt-4.1-mini";

        await viewModel.TestAiConnectionCommand.Execute().FirstAsync();

        viewModel.IsAiProfileValid.Should().BeTrue();
        viewModel.AiProfileStatus.Should().Contain("Planner returned 42 live models");
        connectionTestService.Requests.Should().ContainSingle(request =>
            request.Provider == ModelProviderType.OpenAI
            && request.ApiKey == "sk-planner"
            && request.Endpoint == "https://api.openai.com/v1");
    }

    [Fact]
    public async Task TestAiConnectionCommand_LiveConnectionFails_ShowsProviderFailure()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = CreateViewModel(
            settingsService,
            new StubModelConnectionTestService(ModelConnectionTestResult.Failed("HTTP 401 Unauthorized")));
        viewModel.AiProvider = "OpenAI";
        viewModel.AiApiKey = "sk-invalid";
        viewModel.AiModelId = "gpt-4.1-mini";

        await viewModel.TestAiConnectionCommand.Execute().FirstAsync();

        viewModel.IsAiProfileValid.Should().BeFalse();
        viewModel.AiProfileStatus.Should().Be("Planner connection failed: HTTP 401 Unauthorized");
    }

    [Fact]
    public async Task RefreshPlannerModelsAsync_OpenAiProvider_LoadsModelsFromCatalog()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(
            settingsService,
            new StubModelCatalogService([
                new ModelCatalogEntry
                {
                    Provider = ModelProviderType.OpenAI,
                    ProviderId = "openai",
                    ModelId = "gpt-5.2",
                    DisplayName = "GPT-5.2",
                    Source = "provider-live+models.dev",
                    IsAvailable = true,
                    Capabilities = ["reasoning", "tool-calling"]
                },
                new ModelCatalogEntry
                {
                    Provider = ModelProviderType.OpenAI,
                    ProviderId = "openai",
                    ModelId = "gpt-4.1-mini",
                    DisplayName = "GPT-4.1 mini",
                    Source = "provider-live+models.dev",
                    IsAvailable = true,
                    InputPricePerMillionTokens = 0.4m,
                    OutputPricePerMillionTokens = 1.6m
                }
            ]))
        {
            AiProvider = "OpenAI",
            AiApiKey = "sk-test"
        };

        await viewModel.RefreshPlannerModelsAsync();

        viewModel.AiEndpoint.Should().Be("https://api.openai.com/v1");
        viewModel.AiModelOptions.Should().Equal("gpt-5.2", "gpt-4.1-mini");
        viewModel.AiModelCatalogEntries.Should().ContainSingle(model =>
            model.ModelId == "gpt-4.1-mini"
            && model.DisplayName == "GPT-4.1 mini"
            && model.InputPricePerMillionTokens == 0.4m);
        viewModel.AiModelId.Should().Be("gpt-4.1-mini");
        viewModel.IsPlannerModelCatalogBacked.Should().BeTrue();
    }

    private static SettingsViewModel CreateViewModel(
        ISettingsService settingsService,
        IModelConnectionTestService? connectionTestService = null)
    {
        return new SettingsViewModel(
            settingsService,
            new StubModelCatalogService([]),
            connectionTestService ?? new StubModelConnectionTestService(ModelConnectionTestResult.Passed(12)));
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

    private sealed class StubModelConnectionTestService(ModelConnectionTestResult result) : IModelConnectionTestService
    {
        public List<ModelProviderProfile> Requests { get; } = [];

        public Task<ModelConnectionTestResult> TestAsync(
            ModelProviderProfile profile,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(profile);
            return Task.FromResult(result);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, true);
        }
    }
}
