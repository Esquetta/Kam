namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubWorkflowJobLogResult(
    bool Success,
    string Message,
    string RepositoryFullName,
    long JobId,
    string DownloadUrl,
    string LogPreview,
    IReadOnlyList<string>? MissingSettings = null)
{
    public static GitHubWorkflowJobLogResult Succeeded(
        string message,
        string repositoryFullName,
        long jobId,
        string downloadUrl,
        string logPreview)
    {
        return new GitHubWorkflowJobLogResult(
            true,
            message,
            repositoryFullName,
            jobId,
            downloadUrl,
            logPreview);
    }

    public static GitHubWorkflowJobLogResult Failed(
        string message,
        IReadOnlyList<string>? missingSettings = null)
    {
        return new GitHubWorkflowJobLogResult(
            false,
            message,
            string.Empty,
            0,
            string.Empty,
            string.Empty,
            missingSettings);
    }
}
