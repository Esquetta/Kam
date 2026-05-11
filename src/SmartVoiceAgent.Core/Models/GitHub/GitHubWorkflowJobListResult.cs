namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubWorkflowJobListResult(
    bool Success,
    string Message,
    string RepositoryFullName,
    long RunId,
    IReadOnlyList<GitHubWorkflowJobSummary> Jobs,
    IReadOnlyList<string>? MissingSettings = null)
{
    public static GitHubWorkflowJobListResult Succeeded(
        string message,
        string repositoryFullName,
        long runId,
        IReadOnlyList<GitHubWorkflowJobSummary> jobs)
    {
        return new GitHubWorkflowJobListResult(true, message, repositoryFullName, runId, jobs);
    }

    public static GitHubWorkflowJobListResult Failed(
        string message,
        IReadOnlyList<string>? missingSettings = null)
    {
        return new GitHubWorkflowJobListResult(false, message, string.Empty, 0, [], missingSettings);
    }
}
