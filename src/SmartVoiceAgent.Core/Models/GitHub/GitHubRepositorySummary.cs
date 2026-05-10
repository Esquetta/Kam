namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubRepositorySummary(
    string FullName,
    bool IsPrivate,
    string DefaultBranch,
    string HtmlUrl,
    string CloneUrl);
