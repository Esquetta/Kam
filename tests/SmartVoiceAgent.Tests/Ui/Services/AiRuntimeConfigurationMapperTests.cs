using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public class AiRuntimeConfigurationMapperTests
{
    [Fact]
    public void CreateAiServiceOverrides_UsesActiveEnabledPlannerProfile()
    {
        var overrides = AiRuntimeConfigurationMapper.CreateAiServiceOverrides(
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
                }
            ],
            "openrouter-primary");

        overrides.Should().Contain("AIService:Provider", "OpenRouter");
        overrides.Should().Contain("AIService:Endpoint", "https://openrouter.ai/api/v1");
        overrides.Should().Contain("AIService:ApiKey", "sk-test");
        overrides.Should().Contain("AIService:ModelId", "openai/gpt-4.1-mini");
    }

    [Fact]
    public void CreateAiServiceOverrides_EmitsPlannerAndChatSectionsSeparately()
    {
        var overrides = AiRuntimeConfigurationMapper.CreateAiServiceOverrides(
            [
                new ModelProviderProfile
                {
                    Id = "planner",
                    Provider = ModelProviderType.OpenRouter,
                    Endpoint = "https://openrouter.ai/api/v1",
                    ApiKey = "sk-planner",
                    ModelId = "openai/gpt-4.1-mini",
                    Roles = [ModelProviderRole.Planner],
                    Enabled = true
                },
                new ModelProviderProfile
                {
                    Id = "chat",
                    Provider = ModelProviderType.OpenAICompatible,
                    Endpoint = "https://api.example.com/v1",
                    ApiKey = "sk-chat",
                    ModelId = "custom/chat-model",
                    Roles = [ModelProviderRole.Chat],
                    Enabled = true
                }
            ],
            "planner",
            "chat");

        overrides.Should().Contain("AIService:Planner:ModelId", "openai/gpt-4.1-mini");
        overrides.Should().Contain("AIService:Planner:ApiKey", "sk-planner");
        overrides.Should().Contain("AIService:Chat:Provider", "OpenAICompatible");
        overrides.Should().Contain("AIService:Chat:Endpoint", "https://api.example.com/v1");
        overrides.Should().Contain("AIService:Chat:ApiKey", "sk-chat");
        overrides.Should().Contain("AIService:Chat:ModelId", "custom/chat-model");
        overrides.Should().Contain("AIService:ModelId", "openai/gpt-4.1-mini");
    }

    [Fact]
    public void CreateAiServiceOverrides_OllamaProfile_UsesLocalApiKeyFallback()
    {
        var overrides = AiRuntimeConfigurationMapper.CreateAiServiceOverrides(
            [
                new ModelProviderProfile
                {
                    Id = "ollama-local",
                    Provider = ModelProviderType.Ollama,
                    Endpoint = "http://localhost:11434/v1",
                    ModelId = "llama3.1",
                    Roles = [ModelProviderRole.Planner],
                    Enabled = true
                }
            ],
            "ollama-local");

        overrides.Should().Contain("AIService:Provider", "Ollama");
        overrides.Should().Contain("AIService:ApiKey", "ollama");
    }

    [Fact]
    public void CreateAiServiceOverrides_InvalidProfile_ReturnsEmptyOverrides()
    {
        var overrides = AiRuntimeConfigurationMapper.CreateAiServiceOverrides(
            [
                new ModelProviderProfile
                {
                    Id = "broken",
                    Provider = ModelProviderType.OpenRouter,
                    Endpoint = "not-a-url",
                    ApiKey = "sk-test",
                    ModelId = "openai/gpt-4.1-mini",
                    Roles = [ModelProviderRole.Planner],
                    Enabled = true
                }
            ],
            "broken");

        overrides.Should().BeEmpty();
    }

    [Fact]
    public void CreateIntegrationOverrides_TodoistKey_MapsMcpOptions()
    {
        using var settings = new JsonSettingsService(CreateTempSettingsDirectory());
        settings.TodoistApiKey = "todoist-test-token";

        var overrides = AiRuntimeConfigurationMapper.CreateIntegrationOverrides(settings);

        overrides.Should().Contain("McpOptions:TodoistApiKey", "todoist-test-token");
        overrides.Should().Contain("McpOptions:TodoistServerLink", AiRuntimeConfigurationMapper.DefaultTodoistMcpServerLink);
    }

    [Fact]
    public void CreateIntegrationOverrides_EmailSettings_MapsMailRuntimeConfiguration()
    {
        using var settings = new JsonSettingsService(CreateTempSettingsDirectory());
        settings.EmailProvider = "Gmail";
        settings.SmtpHost = "smtp.gmail.com";
        settings.SmtpPort = 587;
        settings.SmtpUsername = "user@example.com";
        settings.SmtpPassword = "app-password";
        settings.SenderEmail = "sender@example.com";
        settings.SmtpEnableSsl = true;

        var overrides = AiRuntimeConfigurationMapper.CreateIntegrationOverrides(settings);

        overrides.Should().Contain("Email:Provider", "Gmail");
        overrides.Should().Contain("Email:Host", "smtp.gmail.com");
        overrides.Should().Contain("Email:SmtpHost", "smtp.gmail.com");
        overrides.Should().Contain("Email:Port", "587");
        overrides.Should().Contain("Email:SmtpPort", "587");
        overrides.Should().Contain("Email:Username", "user@example.com");
        overrides.Should().Contain("Email:Password", "app-password");
        overrides.Should().Contain("Email:AppPassword", "app-password");
        overrides.Should().Contain("Email:FromAddress", "sender@example.com");
        overrides.Should().Contain("Email:EnableSsl", "True");
    }

    [Fact]
    public void CreateIntegrationOverrides_CompleteTwilioSettings_MapsSmsRuntimeConfiguration()
    {
        using var settings = new JsonSettingsService(CreateTempSettingsDirectory());
        settings.TwilioAccountSid = "AC123";
        settings.TwilioAuthToken = "twilio-token";
        settings.TwilioPhoneNumber = "+15551234567";

        var overrides = AiRuntimeConfigurationMapper.CreateIntegrationOverrides(settings);

        overrides.Should().Contain("Sms:Provider", "Twilio");
        overrides.Should().Contain("Sms:TwilioAccountSid", "AC123");
        overrides.Should().Contain("Sms:TwilioAuthToken", "twilio-token");
        overrides.Should().Contain("Sms:TwilioPhoneNumber", "+15551234567");
    }

    private static string CreateTempSettingsDirectory()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "kam-runtime-mapper-tests",
            Guid.NewGuid().ToString("N"));
    }
}
