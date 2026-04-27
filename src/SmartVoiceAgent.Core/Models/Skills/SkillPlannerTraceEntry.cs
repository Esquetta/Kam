namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillPlannerTraceEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string UserRequest { get; init; } = string.Empty;

    public string SystemPrompt { get; init; } = string.Empty;

    public string RawResponse { get; init; } = string.Empty;

    public bool IsValid { get; init; }

    public string SkillId { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public bool RequiresConfirmation { get; init; }

    public string Reasoning { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public long DurationMilliseconds { get; init; }

    public int AvailableSkillCount { get; init; }
}
