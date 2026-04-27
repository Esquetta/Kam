namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillExecutionHistoryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string SkillId { get; init; } = string.Empty;

    public string ArgumentsSummary { get; init; } = string.Empty;

    public bool Success { get; init; }

    public SkillExecutionStatus Status { get; init; } = SkillExecutionStatus.Failed;

    public string ErrorCode { get; init; } = string.Empty;

    public string ResultSummary { get; init; } = string.Empty;

    public long DurationMilliseconds { get; init; }

    public string Command { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public int? ExitCode { get; init; }

    public string StdOut { get; init; } = string.Empty;

    public string StdErr { get; init; } = string.Empty;

    public bool TimedOut { get; init; }

    public bool Cancelled { get; init; }

    public bool Truncated { get; init; }
}
