namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubWorkflowRunListResult(
    bool Success,
    string Message,
    IReadOnlyList<GitHubWorkflowRunSummary> WorkflowRuns,
    IReadOnlyList<string>? MissingSettings = null)
{
    public static GitHubWorkflowRunListResult Succeeded(
        string message,
        IReadOnlyList<GitHubWorkflowRunSummary> workflowRuns)
    {
        return new GitHubWorkflowRunListResult(true, message, workflowRuns);
    }

    public static GitHubWorkflowRunListResult Failed(
        string message,
        IReadOnlyList<string>? missingSettings = null)
    {
        return new GitHubWorkflowRunListResult(false, message, [], missingSettings);
    }
}
