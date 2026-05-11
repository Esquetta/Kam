namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubWorkflowRunSummary(
    string RepositoryFullName,
    long Id,
    string Name,
    string DisplayTitle,
    string Status,
    string Conclusion,
    string Event,
    string HeadBranch,
    string HtmlUrl,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);
