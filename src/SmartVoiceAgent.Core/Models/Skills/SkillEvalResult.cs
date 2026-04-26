namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillEvalResult
{
    public string Name { get; set; } = string.Empty;

    public string SkillId { get; set; } = string.Empty;

    public bool Passed { get; set; }

    public SkillExecutionStatus ExpectedStatus { get; set; }

    public SkillExecutionStatus ActualStatus { get; set; }

    public string Message { get; set; } = string.Empty;

    public long DurationMilliseconds { get; set; }
}
