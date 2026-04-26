namespace SmartVoiceAgent.Core.Models.Skills;

public sealed record SkillPlanParseResult(bool IsValid, SkillPlan? Plan, string ErrorMessage)
{
    public static SkillPlanParseResult Success(SkillPlan plan) => new(true, plan, string.Empty);

    public static SkillPlanParseResult Failure(string errorMessage) => new(false, null, errorMessage);
}
