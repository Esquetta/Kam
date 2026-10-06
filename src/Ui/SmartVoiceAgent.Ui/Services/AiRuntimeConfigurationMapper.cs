using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public static class AiRuntimeConfigurationMapper
{
    public const string DefaultTodoistMcpServerLink = "https://todoist.mcpverse.dev/mcp";

    /// <summary>
    /// Creates every configuration value Settings own: model profiles and integrations.
    /// </summary>
    /// <param name="settings">The saved settings.</param>
    public static IReadOnlyDictionary<string, string?> CreateOverrides(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in CreateAiServiceOverrides(
                     settings.ModelProviderProfiles,
                     settings.ActivePlannerProfileId,
                     settings.ActiveChatProfileId))
        {
            overrides[item.Key] = item.Value;
        }

        foreach (var item in CreateIntegrationOverrides(settings))
        {
            overrides[item.Key] = item.Value;
        }

        return overrides;
    }

    public static IReadOnlyDictionary<string, string?> CreateAiServiceOverrides(
        IReadOnlyList<ModelProviderProfile> profiles,
        string activePlannerProfileId,
        string activeChatProfileId = "")
    {
        var plannerProfile = SelectProfile(profiles, activePlannerProfileId, ModelProviderRole.Planner);
        var chatProfile = SelectProfile(profiles, activeChatProfileId, ModelProviderRole.Chat);

        // Either usable profile runs everything; a single working key is enough to chat.
        var usablePlanner = IsUsableProfile(plannerProfile) ? plannerProfile : null;
        var usableChat = IsUsableProfile(chatProfile) ? chatProfile : null;
        if (usablePlanner is null && usableChat is null)
        {
            return new Dictionary<string, string?>();
        }

        var basePlanner = usablePlanner ?? usableChat!;
        var overrides = new Dictionary<string, string?>();
        AddProfileOverrides(overrides, "AIService", basePlanner);
        AddProfileOverrides(overrides, "AIService:Planner", basePlanner);

        var agentProfile = usableChat ?? basePlanner;
        AddProfileOverrides(
            overrides,
            "AIService:Chat",
            agentProfile);
        AddProfileOverrides(overrides, "AIService:Agents", agentProfile);

        return overrides;
    }

    /// <summary>
    /// Returns the profile chat runs on: the chat profile when it is usable, otherwise the planner profile,
    /// or null when neither has what it needs.
    /// </summary>
    public static ModelProviderProfile? ResolveChatProfile(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var profiles = settings.ModelProviderProfiles;
        var chatProfile = SelectProfile(profiles, settings.ActiveChatProfileId, ModelProviderRole.Chat);
        if (IsUsableProfile(chatProfile))
        {
            return chatProfile;
        }

        var plannerProfile = SelectProfile(profiles, settings.ActivePlannerProfileId, ModelProviderRole.Planner);
        return IsUsableProfile(plannerProfile) ? plannerProfile : null;
    }

    private static bool IsUsableProfile([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] ModelProviderProfile? profile)
    {
        return profile is not null
            && profile.Enabled
            && profile.Validate().IsValid;
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

        AddIfNotBlank(overrides, "WebResearch:SearchApiKey", settings.WebSearchApiKey?.Trim());
        AddIfNotBlank(overrides, "WebResearch:SearchEngineId", settings.WebSearchEngineId?.Trim());

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

        if (!string.IsNullOrWhiteSpace(settings.GitHubAppId)
            && !string.IsNullOrWhiteSpace(settings.GitHubAppInstallationId)
            && !string.IsNullOrWhiteSpace(settings.GitHubAppPrivateKeyPath))
        {
            overrides["GitHubApp:AppId"] = settings.GitHubAppId;
            overrides["GitHubApp:InstallationId"] = settings.GitHubAppInstallationId;
            overrides["GitHubApp:PrivateKeyPath"] = settings.GitHubAppPrivateKeyPath;
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
