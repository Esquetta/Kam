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
        settingsService.ModelProviderProfiles.Should().ContainSingle(
            p => p.Provider == ModelProviderType.OpenRouter && p.Roles.Contains(ModelProviderRole.Planner));
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, true);
        }
    }
}
