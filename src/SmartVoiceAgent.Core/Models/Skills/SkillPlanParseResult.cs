namespace SmartVoiceAgent.Core.Models.Skills;

public sealed record SkillPlanParseResult(bool IsValid, SkillPlan? Plan, string ErrorMessage)
{
    public bool Succeeded => IsValid;

    public string SanitizedRawResponse { get; init; } = string.Empty;

    public static SkillPlanParseResult Success(
        SkillPlan plan,
        string sanitizedRawResponse = "") =>
        new(true, plan, string.Empty)
        {
            SanitizedRawResponse = sanitizedRawResponse
        };

    public static SkillPlanParseResult Failure(
        string errorMessage,
        string sanitizedRawResponse = "",
        SkillPlan? plan = null) =>
        new(false, plan, errorMessage)
        {
            SanitizedRawResponse = sanitizedRawResponse
        };
}
