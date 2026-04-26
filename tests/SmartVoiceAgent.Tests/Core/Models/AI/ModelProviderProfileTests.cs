using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Tests.Core.Models.AI;

public class ModelProviderProfileTests
{
    [Fact]
    public void Validate_EnabledOpenRouterProfileWithPlannerRole_ReturnsSuccess()
    {
        var profile = new ModelProviderProfile
        {
            Id = "openrouter-primary",
            Provider = ModelProviderType.OpenRouter,
            DisplayName = "OpenRouter Primary",
            Endpoint = "https://openrouter.ai/api/v1",
            ApiKey = "sk-test",
            ModelId = "openai/gpt-4.1-mini",
            Roles = [ModelProviderRole.Planner],
            Enabled = true
        };

        var result = profile.Validate();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EnabledProfileWithoutModelId_ReturnsActionableError()
    {
        var profile = new ModelProviderProfile
        {
            Id = "openrouter-primary",
            Provider = ModelProviderType.OpenRouter,
            Endpoint = "https://openrouter.ai/api/v1",
            ApiKey = "sk-test",
            Roles = [ModelProviderRole.Planner],
            Enabled = true
        };

        var result = profile.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Model id is required.");
    }

    [Fact]
    public void MaskedApiKey_DoesNotExposeSecret()
    {
        var profile = new ModelProviderProfile { ApiKey = "sk-1234567890abcdef" };

        profile.MaskedApiKey.Should().Be("sk-1***********cdef");
    }
}
