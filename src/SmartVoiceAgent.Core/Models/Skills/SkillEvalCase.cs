namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillEvalCase
{
    public string Name { get; set; } = string.Empty;

    public SkillPlan Plan { get; set; } = new();

    public SkillExecutionStatus ExpectedStatus { get; set; } = SkillExecutionStatus.Succeeded;
}
