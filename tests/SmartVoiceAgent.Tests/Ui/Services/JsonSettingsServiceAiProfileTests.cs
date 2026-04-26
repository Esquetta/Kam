using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public class JsonSettingsServiceAiProfileTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoadModelProviderProfiles_PreservesActivePlannerAndChatProfiles()
    {
        using (var service = new JsonSettingsService(_settingsDirectory))
        {
            service.ModelProviderProfiles =
            [
                new ModelProviderProfile
                {
                    Id = "openrouter-primary",
                    Provider = ModelProviderType.OpenRouter,
                    Endpoint = "https://openrouter.ai/api/v1",
                    ApiKey = "sk-test",
                    ModelId = "openai/gpt-4.1-mini",
                    Roles = [ModelProviderRole.Planner],
                    Enabled = true
                },
                new ModelProviderProfile
                {
                    Id = "openrouter-chat",
                    Provider = ModelProviderType.OpenRouter,
                    Endpoint = "https://openrouter.ai/api/v1",
                    ApiKey = "sk-chat",
                    ModelId = "openai/gpt-4.1-mini",
                    Roles = [ModelProviderRole.Chat],
                    Enabled = true
                }
            ];
            service.ActivePlannerProfileId = "openrouter-primary";
            service.ActiveChatProfileId = "openrouter-chat";
            service.Save();
        }

        using var reloaded = new JsonSettingsService(_settingsDirectory);

        reloaded.ActivePlannerProfileId.Should().Be("openrouter-primary");
        reloaded.ActiveChatProfileId.Should().Be("openrouter-chat");
        reloaded.ModelProviderProfiles.Should().ContainSingle(
            p => p.Id == "openrouter-primary" && p.ModelId == "openai/gpt-4.1-mini");
        reloaded.ModelProviderProfiles.Should().ContainSingle(
            p => p.Id == "openrouter-chat" && p.Roles.Contains(ModelProviderRole.Chat));
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, true);
        }
    }
}
