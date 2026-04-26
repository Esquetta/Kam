namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillEvalSummary
{
    public int Total { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public IReadOnlyList<SkillEvalResult> Results { get; set; } = [];
}
