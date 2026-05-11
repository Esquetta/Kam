namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubWorkflowJobSummary(
    string RepositoryFullName,
    long RunId,
    long Id,
    string Name,
    string Status,
    string Conclusion,
    string HtmlUrl,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public IReadOnlyList<GitHubWorkflowJobStepSummary> Steps { get; init; } = [];
}
