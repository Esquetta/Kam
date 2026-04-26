namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillHealthReport
{
    public string SkillId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string ExecutorType { get; set; } = string.Empty;

    public SkillRiskLevel RiskLevel { get; set; } = SkillRiskLevel.Low;

    public SkillHealthStatus Status { get; set; } = SkillHealthStatus.Healthy;

    public string Details { get; set; } = string.Empty;
}
