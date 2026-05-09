namespace SmartVoiceAgent.Core.Models.Updates;

public sealed record ApplicationUpdateCheckResult(
    bool Success,
    bool IsUpdateAvailable,
    string Message,
    string CurrentVersion,
    string? LatestVersion = null,
    string? ReleaseName = null,
    string? ReleaseUrl = null,
    DateTimeOffset? PublishedAt = null,
    ApplicationUpdateAsset? Asset = null)
{
    public static ApplicationUpdateCheckResult Failed(string currentVersion, string message)
    {
        return new ApplicationUpdateCheckResult(false, false, message, currentVersion);
    }

    public static ApplicationUpdateCheckResult UpToDate(
        string currentVersion,
        string latestVersion,
        string? releaseName,
        string? releaseUrl,
        DateTimeOffset? publishedAt)
    {
        return new ApplicationUpdateCheckResult(
            true,
            false,
            $"Kam is up to date ({currentVersion}).",
            currentVersion,
            latestVersion,
            releaseName,
            releaseUrl,
            publishedAt);
    }

    public static ApplicationUpdateCheckResult UpdateAvailable(
        string currentVersion,
        string latestVersion,
        string? releaseName,
        string? releaseUrl,
        DateTimeOffset? publishedAt,
        ApplicationUpdateAsset? asset)
    {
        return new ApplicationUpdateCheckResult(
            true,
            true,
            $"Kam {latestVersion} is available.",
            currentVersion,
            latestVersion,
            releaseName,
            releaseUrl,
            publishedAt,
            asset);
    }
}
