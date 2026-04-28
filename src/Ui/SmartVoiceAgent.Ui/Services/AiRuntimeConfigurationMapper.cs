using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public static class AiRuntimeConfigurationMapper
{
    public const string DefaultTodoistMcpServerLink = "https://todoist.mcpverse.dev/mcp";

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

    public static IReadOnlyDictionary<string, string?> CreateIntegrationOverrides(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var overrides = new Dictionary<string, string?>();

        if (!string.IsNullOrWhiteSpace(settings.TodoistApiKey))
        {
            overrides["McpOptions:TodoistApiKey"] = settings.TodoistApiKey;
            overrides["McpOptions:TodoistServerLink"] = DefaultTodoistMcpServerLink;
        }

        if (!string.IsNullOrWhiteSpace(settings.SmtpUsername)
            && !string.IsNullOrWhiteSpace(settings.SmtpPassword))
        {
            AddIfNotBlank(overrides, "Email:Provider", settings.EmailProvider);
            AddIfNotBlank(overrides, "Email:Host", settings.SmtpHost);
            AddIfNotBlank(overrides, "Email:SmtpHost", settings.SmtpHost);
            overrides["Email:Port"] = settings.SmtpPort.ToString(CultureInfo.InvariantCulture);
            overrides["Email:SmtpPort"] = settings.SmtpPort.ToString(CultureInfo.InvariantCulture);
            overrides["Email:Username"] = settings.SmtpUsername;
            overrides["Email:Password"] = settings.SmtpPassword;
            overrides["Email:AppPassword"] = settings.SmtpPassword;
            AddIfNotBlank(overrides, "Email:FromAddress", settings.SenderEmail);
            AddIfNotBlank(overrides, "Email:FromName", "Kam");
            overrides["Email:EnableSsl"] = settings.SmtpEnableSsl.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(settings.TwilioAccountSid)
            && !string.IsNullOrWhiteSpace(settings.TwilioAuthToken)
            && !string.IsNullOrWhiteSpace(settings.TwilioPhoneNumber))
        {
            overrides["Sms:Provider"] = "Twilio";
            overrides["Sms:TwilioAccountSid"] = settings.TwilioAccountSid;
            overrides["Sms:TwilioAuthToken"] = settings.TwilioAuthToken;
            overrides["Sms:TwilioPhoneNumber"] = settings.TwilioPhoneNumber;
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

    private static void AddIfNotBlank(
        IDictionary<string, string?> overrides,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            overrides[key] = value;
        }
    }
}
