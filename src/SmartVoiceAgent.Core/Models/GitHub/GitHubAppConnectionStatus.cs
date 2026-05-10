namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubAppConnectionStatus(
    bool IsConfigured,
    bool IsConnected,
    string Message,
    string? AppId = null,
    string? InstallationId = null,
    string ApiBaseUrl = "https://api.github.com",
    string? AppName = null,
    string? AppSlug = null,
    int? RepositoryCount = null,
    IReadOnlyList<string>? MissingSettings = null)
{
    public static GitHubAppConnectionStatus NotConfigured(
        string message,
        IReadOnlyList<string> missingSettings)
    {
        return new GitHubAppConnectionStatus(
            false,
            false,
            message,
            MissingSettings: missingSettings);
    }

    public static GitHubAppConnectionStatus Failed(
        string message,
        string? appId,
        string? installationId,
        string apiBaseUrl)
    {
        return new GitHubAppConnectionStatus(
            true,
            false,
            message,
            appId,
            installationId,
            apiBaseUrl);
    }

    public static GitHubAppConnectionStatus Connected(
        string appId,
        string installationId,
        string apiBaseUrl,
        string? appName,
        string? appSlug,
        int? repositoryCount)
    {
        return new GitHubAppConnectionStatus(
            true,
            true,
            "GitHub App is connected.",
            appId,
            installationId,
            apiBaseUrl,
            appName,
            appSlug,
            repositoryCount);
    }
}
