namespace SmartVoiceAgent.Core.Models.Updates;

public sealed class ApplicationUpdateOptions
{
    public const string SectionName = "ApplicationUpdates";

    public string Owner { get; set; } = "Esquetta";

    public string Repository { get; set; } = "Kam";

    public string Channel { get; set; } = "stable";

    public string? PreferredAssetName { get; set; }

    public string? DownloadDirectory { get; set; }

    public bool IncludePrerelease { get; set; }

    public Uri GetLatestReleaseUri()
    {
        var owner = string.IsNullOrWhiteSpace(Owner) ? "Esquetta" : Owner.Trim();
        var repository = string.IsNullOrWhiteSpace(Repository) ? "Kam" : Repository.Trim();
        return new Uri($"https://api.github.com/repos/{owner}/{repository}/releases/latest");
    }

    public string GetRepositorySlug()
    {
        var owner = string.IsNullOrWhiteSpace(Owner) ? "Esquetta" : Owner.Trim();
        var repository = string.IsNullOrWhiteSpace(Repository) ? "Kam" : Repository.Trim();
        return $"{owner}/{repository}";
    }
}
