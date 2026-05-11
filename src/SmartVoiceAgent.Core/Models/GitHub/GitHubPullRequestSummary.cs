namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubPullRequestSummary(
    string RepositoryFullName,
    int Number,
    string Title,
    string State,
    string AuthorLogin,
    string HtmlUrl,
    string HeadRefName,
    string BaseRefName,
    bool IsDraft,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);
