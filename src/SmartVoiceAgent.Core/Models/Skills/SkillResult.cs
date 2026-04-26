namespace SmartVoiceAgent.Core.Models.Skills;

public sealed record SkillResult(bool Success, string Message, string ErrorMessage, object? Data = null)
{
    public static SkillResult Succeeded(string message, object? data = null) => new(true, message, string.Empty, data);

    public static SkillResult Failed(string errorMessage) => new(false, string.Empty, errorMessage);
}
