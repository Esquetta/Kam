namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubPullRequestListResult(
    bool Success,
    string Message,
    IReadOnlyList<GitHubPullRequestSummary> PullRequests,
    IReadOnlyList<string>? MissingSettings = null)
{
    public static GitHubPullRequestListResult Succeeded(
        string message,
        IReadOnlyList<GitHubPullRequestSummary> pullRequests)
    {
        return new GitHubPullRequestListResult(true, message, pullRequests);
    }

    public static GitHubPullRequestListResult Failed(
        string message,
        IReadOnlyList<string>? missingSettings = null)
    {
        return new GitHubPullRequestListResult(false, message, [], missingSettings);
    }
}
