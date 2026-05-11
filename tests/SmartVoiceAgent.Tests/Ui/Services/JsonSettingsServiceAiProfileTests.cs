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
            service.TodoistApiKey = "todoist-secret";
            service.SmtpPassword = "smtp-secret";
            service.TwilioAuthToken = "twilio-secret";
            service.Save();
        }

        using var reloaded = new JsonSettingsService(_settingsDirectory);

        reloaded.ActivePlannerProfileId.Should().Be("openrouter-primary");
        reloaded.ActiveChatProfileId.Should().Be("openrouter-chat");
        reloaded.ModelProviderProfiles.Should().ContainSingle(
            p => p.Id == "openrouter-primary"
                && p.ModelId == "openai/gpt-4.1-mini"
                && p.ApiKey == "sk-test");
        reloaded.ModelProviderProfiles.Should().ContainSingle(
            p => p.Id == "openrouter-chat"
                && p.ApiKey == "sk-chat"
                && p.Roles.Contains(ModelProviderRole.Chat));
        reloaded.TodoistApiKey.Should().Be("todoist-secret");
        reloaded.SmtpPassword.Should().Be("smtp-secret");
        reloaded.TwilioAuthToken.Should().Be("twilio-secret");

        var settingsJson = File.ReadAllText(Path.Combine(_settingsDirectory, "settings.json"));
        settingsJson.Should().NotContain("sk-test");
        settingsJson.Should().NotContain("sk-chat");
        settingsJson.Should().NotContain("todoist-secret");
        settingsJson.Should().NotContain("smtp-secret");
        settingsJson.Should().NotContain("twilio-secret");
        settingsJson.Should().NotContain("\"TodoistApiKey\"");
        settingsJson.Should().NotContain("\"SmtpPassword\"");
        settingsJson.Should().NotContain("\"TwilioAuthToken\"");
        settingsJson.Should().NotContain("\"ApiKey\"");

        var secretStoreJson = File.ReadAllText(Path.Combine(_settingsDirectory, "settings.secrets.json"));
        secretStoreJson.Should().NotContain("sk-test");
        secretStoreJson.Should().NotContain("sk-chat");
        secretStoreJson.Should().NotContain("todoist-secret");
        secretStoreJson.Should().NotContain("smtp-secret");
        secretStoreJson.Should().NotContain("twilio-secret");
    }

    [Fact]
    public void SaveAndLoadGitHubAppSettings_KeepsPrivateKeyPathOutOfSettingsJson()
    {
        const string privateKeyPath = @"C:\secure\kam-github-app.pem";

        using (var service = new JsonSettingsService(_settingsDirectory))
        {
            service.GitHubAppId = "12345";
            service.GitHubAppInstallationId = "98765";
            service.GitHubAppPrivateKeyPath = privateKeyPath;
            service.Save();
        }

        using var reloaded = new JsonSettingsService(_settingsDirectory);

        reloaded.GitHubAppId.Should().Be("12345");
        reloaded.GitHubAppInstallationId.Should().Be("98765");
        reloaded.GitHubAppPrivateKeyPath.Should().Be(privateKeyPath);

        var settingsJson = File.ReadAllText(Path.Combine(_settingsDirectory, "settings.json"));
        settingsJson.Should().Contain("\"GitHubAppId\"");
        settingsJson.Should().Contain("\"GitHubAppInstallationId\"");
        settingsJson.Should().NotContain(privateKeyPath);
        settingsJson.Should().NotContain("\"GitHubAppPrivateKeyPath\"");

        var secretStoreJson = File.ReadAllText(Path.Combine(_settingsDirectory, "settings.secrets.json"));
        secretStoreJson.Should().NotContain(privateKeyPath);
    }

    [Fact]
    public void Load_LegacyPlaintextSecrets_MigratesSecretsOutOfSettingsJson()
    {
        Directory.CreateDirectory(_settingsDirectory);
        File.WriteAllText(
            Path.Combine(_settingsDirectory, "settings.json"),
            """
            {
              "ShowOnStartup": true,
              "TodoistApiKey": "legacy-todoist-secret",
              "GitHubAppPrivateKeyPath": "C:\\secure\\legacy-kam-github-app.pem",
              "SmtpPassword": "legacy-smtp-secret",
              "TwilioAuthToken": "legacy-twilio-secret",
              "ModelProviderProfiles": [
                {
                  "Id": "legacy-openrouter",
                  "Provider": 0,
                  "DisplayName": "Legacy OpenRouter",
                  "Endpoint": "https://openrouter.ai/api/v1",
                  "ApiKey": "legacy-openrouter-secret",
                  "ModelId": "openai/gpt-4.1-mini",
                  "Roles": [0],
                  "Temperature": 0.2,
                  "MaxTokens": 1200,
                  "Enabled": true
                }
              ],
              "ActivePlannerProfileId": "legacy-openrouter",
              "ActiveChatProfileId": "legacy-openrouter"
            }
            """);

        using var service = new JsonSettingsService(_settingsDirectory);

        service.TodoistApiKey.Should().Be("legacy-todoist-secret");
        service.GitHubAppPrivateKeyPath.Should().Be(@"C:\secure\legacy-kam-github-app.pem");
        service.SmtpPassword.Should().Be("legacy-smtp-secret");
        service.TwilioAuthToken.Should().Be("legacy-twilio-secret");
        service.ModelProviderProfiles.Should().ContainSingle(
            p => p.Id == "legacy-openrouter" && p.ApiKey == "legacy-openrouter-secret");

        var settingsJson = File.ReadAllText(Path.Combine(_settingsDirectory, "settings.json"));
        settingsJson.Should().NotContain("legacy-todoist-secret");
        settingsJson.Should().NotContain(@"C:\secure\legacy-kam-github-app.pem");
        settingsJson.Should().NotContain("legacy-smtp-secret");
        settingsJson.Should().NotContain("legacy-twilio-secret");
        settingsJson.Should().NotContain("legacy-openrouter-secret");
        settingsJson.Should().NotContain("\"TodoistApiKey\"");
        settingsJson.Should().NotContain("\"GitHubAppPrivateKeyPath\"");
        settingsJson.Should().NotContain("\"SmtpPassword\"");
        settingsJson.Should().NotContain("\"TwilioAuthToken\"");
        settingsJson.Should().NotContain("\"ApiKey\"");
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, true);
        }
    }
}
