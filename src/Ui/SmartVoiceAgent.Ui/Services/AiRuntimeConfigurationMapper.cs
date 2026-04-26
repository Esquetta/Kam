using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public static class AiRuntimeConfigurationMapper
{
    public static IReadOnlyDictionary<string, string?> CreateAiServiceOverrides(
        IReadOnlyList<ModelProviderProfile> profiles,
        string activePlannerProfileId)
    {
        var profile = SelectPlannerProfile(profiles, activePlannerProfileId);
        if (profile is null || !profile.Enabled || !profile.Validate().IsValid)
        {
            return new Dictionary<string, string?>();
        }

        return new Dictionary<string, string?>
        {
            ["AIService:Provider"] = profile.Provider.ToString(),
            ["AIService:Endpoint"] = profile.Endpoint,
            ["AIService:ApiKey"] = ResolveApiKey(profile),
            ["AIService:ModelId"] = profile.ModelId,
            ["AIService:DefaultTemperature"] = profile.Temperature.ToString(CultureInfo.InvariantCulture),
            ["AIService:DefaultMaxTokens"] = profile.MaxTokens.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static ModelProviderProfile? SelectPlannerProfile(
        IReadOnlyList<ModelProviderProfile> profiles,
        string activePlannerProfileId)
    {
        return profiles.FirstOrDefault(profile =>
                profile.Id.Equals(activePlannerProfileId, StringComparison.OrdinalIgnoreCase)
                && profile.Roles.Contains(ModelProviderRole.Planner))
            ?? profiles.FirstOrDefault(profile => profile.Roles.Contains(ModelProviderRole.Planner));
    }

    private static string ResolveApiKey(ModelProviderProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            return profile.ApiKey;
        }

        return profile.Provider == ModelProviderType.Ollama ? "ollama" : string.Empty;
    }
}
