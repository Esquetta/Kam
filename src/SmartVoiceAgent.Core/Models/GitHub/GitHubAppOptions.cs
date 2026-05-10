namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed class GitHubAppOptions
{
    public const string SectionName = "GitHubApp";

    public string AppId { get; set; } = string.Empty;

    public string InstallationId { get; set; } = string.Empty;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    public string ApiVersion { get; set; } = "2026-03-10";

    public IReadOnlyList<string> GetMissingSettings()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(AppId))
        {
            missing.Add($"{SectionName}:AppId");
        }

        if (string.IsNullOrWhiteSpace(InstallationId))
        {
            missing.Add($"{SectionName}:InstallationId");
        }

        if (string.IsNullOrWhiteSpace(PrivateKeyPath))
        {
            missing.Add($"{SectionName}:PrivateKeyPath");
        }

        return missing;
    }

    public bool IsConfigured => GetMissingSettings().Count == 0;

    public Uri GetApiBaseUri()
    {
        var value = string.IsNullOrWhiteSpace(ApiBaseUrl)
            ? "https://api.github.com"
            : ApiBaseUrl.Trim();
        return new Uri(value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/");
    }
}
