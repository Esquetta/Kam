namespace SmartVoiceAgent.Core.Models.GitHub;

public sealed record GitHubWorkflowJobStepSummary(
    int Number,
    string Name,
    string Status,
    string Conclusion,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
