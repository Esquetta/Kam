using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

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
    public void TestAiConnectionCommand_InvalidEndpoint_ShowsValidationError()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settingsService)
        {
            AiEndpoint = "not-a-url",
            AiApiKey = "sk-test",
            AiModelId = "openai/gpt-4.1-mini"
        };

        viewModel.TestAiConnectionCommand.Execute().Subscribe();

        viewModel.IsAiProfileValid.Should().BeFalse();
        viewModel.AiProfileStatus.Should().Contain("valid endpoint");
    }

    [Fact]
    public void TestAiConnectionCommand_OllamaWithoutApiKey_IsValid()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settingsService)
        {
            AiProvider = "Ollama",
            AiEndpoint = "http://localhost:11434/v1",
            AiApiKey = string.Empty,
            AiModelId = "llama3.1"
        };

        viewModel.TestAiConnectionCommand.Execute().Subscribe();

        viewModel.IsAiProfileValid.Should().BeTrue();
        viewModel.AiProfileStatus.Should().Contain("valid");
        settingsService.ModelProviderProfiles.Should().ContainSingle(p =>
            p.Provider == ModelProviderType.Ollama
            && p.Enabled
            && p.ModelId == "llama3.1");
    }

    [Fact]
    public void ChatProfileSettings_SaveSeparateActiveChatProfile()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settingsService)
        {
            ChatProvider = "OpenAICompatible",
            ChatEndpoint = "https://api.example.com/v1",
            ChatApiKey = "sk-chat",
            ChatModelId = "custom/chat-model",
            ActiveChatProfileId = "custom-chat"
        };

        viewModel.TestAiConnectionCommand.Execute().Subscribe();

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
    public async Task RefreshPlannerModelsAsync_OpenAiProvider_LoadsModelsFromCatalog()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(
            settingsService,
            new StubModelCatalogService("gpt-5.2", "gpt-4.1-mini"))
        {
            AiProvider = "OpenAI",
            AiApiKey = "sk-test"
        };

        await viewModel.RefreshPlannerModelsAsync();

        viewModel.AiEndpoint.Should().Be("https://api.openai.com/v1");
        viewModel.AiModelOptions.Should().Equal("gpt-5.2", "gpt-4.1-mini");
        viewModel.AiModelId.Should().Be("gpt-4.1-mini");
        viewModel.IsPlannerModelCatalogBacked.Should().BeTrue();
    }

    private sealed class StubModelCatalogService(params string[] modelIds) : IModelCatalogService
    {
        public Task<IReadOnlyList<string>> GetModelIdsAsync(
            ModelProviderProfile profile,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(modelIds);
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
