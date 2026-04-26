namespace SmartVoiceAgent.Core.Models.Skills;

public sealed record SkillResult(bool Success, string Message, string ErrorMessage, object? Data = null)
{
    public SkillExecutionStatus Status { get; init; } =
        Success ? SkillExecutionStatus.Succeeded : SkillExecutionStatus.Failed;

    public string ErrorCode { get; init; } = string.Empty;

    public long DurationMilliseconds { get; init; }

    public static SkillResult Succeeded(string message, object? data = null) =>
        new(true, message, string.Empty, data) { Status = SkillExecutionStatus.Succeeded };

    public static SkillResult Failed(
        string errorMessage,
        SkillExecutionStatus status = SkillExecutionStatus.Failed,
        string errorCode = "") =>
        new(false, string.Empty, errorMessage) { Status = status, ErrorCode = errorCode };
}
