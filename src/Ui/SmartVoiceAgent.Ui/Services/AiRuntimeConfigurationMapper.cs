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
        string activePlannerProfileId,
        string activeChatProfileId = "")
    {
        var plannerProfile = SelectProfile(profiles, activePlannerProfileId, ModelProviderRole.Planner);
        if (plannerProfile is null || !plannerProfile.Enabled || !plannerProfile.Validate().IsValid)
        {
            return new Dictionary<string, string?>();
        }

        var overrides = new Dictionary<string, string?>();
        AddProfileOverrides(overrides, "AIService", plannerProfile);
        AddProfileOverrides(overrides, "AIService:Planner", plannerProfile);

        var chatProfile = SelectProfile(profiles, activeChatProfileId, ModelProviderRole.Chat);
        if (chatProfile is not null && chatProfile.Enabled && chatProfile.Validate().IsValid)
        {
            AddProfileOverrides(overrides, "AIService:Chat", chatProfile);
        }

        return overrides;
    }

    private static ModelProviderProfile? SelectProfile(
        IReadOnlyList<ModelProviderProfile> profiles,
        string activeProfileId,
        ModelProviderRole role)
    {
        return profiles.FirstOrDefault(profile =>
                profile.Id.Equals(activeProfileId, StringComparison.OrdinalIgnoreCase)
                && profile.Roles.Contains(role))
            ?? profiles.FirstOrDefault(profile => profile.Roles.Contains(role));
    }

    private static void AddProfileOverrides(
        IDictionary<string, string?> overrides,
        string prefix,
        ModelProviderProfile profile)
    {
        overrides[$"{prefix}:Provider"] = profile.Provider.ToString();
        overrides[$"{prefix}:Endpoint"] = profile.Endpoint;
        overrides[$"{prefix}:ApiKey"] = ResolveApiKey(profile);
        overrides[$"{prefix}:ModelId"] = profile.ModelId;
        overrides[$"{prefix}:DefaultTemperature"] = profile.Temperature.ToString(CultureInfo.InvariantCulture);
        overrides[$"{prefix}:DefaultMaxTokens"] = profile.MaxTokens.ToString(CultureInfo.InvariantCulture);
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
