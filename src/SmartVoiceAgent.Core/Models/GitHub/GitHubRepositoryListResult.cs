namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubRepositoryListResult(
    bool Success,
    string Message,
    IReadOnlyList<GitHubRepositorySummary> Repositories,
    IReadOnlyList<string>? MissingSettings = null)
{
    public static GitHubRepositoryListResult Succeeded(
        string message,
        IReadOnlyList<GitHubRepositorySummary> repositories)
    {
        return new GitHubRepositoryListResult(true, message, repositories);
    }

    public static GitHubRepositoryListResult Failed(
        string message,
        IReadOnlyList<string>? missingSettings = null)
    {
        return new GitHubRepositoryListResult(false, message, [], missingSettings);
    }
}
