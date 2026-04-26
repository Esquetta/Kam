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
}
