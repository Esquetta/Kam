using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Models.Commands;

public sealed record CommandRuntimeResult(bool Success, string Message)
{
    public string SkillId { get; init; } = string.Empty;

    public SkillExecutionStatus Status { get; init; } = SkillExecutionStatus.Failed;

    public string ErrorCode { get; init; } = string.Empty;

    public long DurationMilliseconds { get; init; }

    public bool RequiresConfirmation { get; init; }

    public Guid? ConfirmationId { get; init; }

    public static CommandRuntimeResult Succeeded(string message, string skillId, SkillResult result) =>
        new(true, string.IsNullOrWhiteSpace(message) ? $"Skill {skillId} completed." : message)
        {
            SkillId = skillId,
            Status = result.Status,
            ErrorCode = string.Empty,
            DurationMilliseconds = result.DurationMilliseconds
        };

    public static CommandRuntimeResult PendingConfirmation(
        string message,
        string skillId,
        Guid confirmationId) =>
        new(false, string.IsNullOrWhiteSpace(message) ? $"Skill {skillId} requires confirmation." : message)
        {
            SkillId = skillId,
            Status = SkillExecutionStatus.ValidationFailed,
            ErrorCode = "confirmation_required",
            RequiresConfirmation = true,
            ConfirmationId = confirmationId
        };

    public static CommandRuntimeResult Failed(
        string message,
        SkillExecutionStatus status,
        string errorCode,
        string skillId = "",
        long durationMilliseconds = 0) =>
        new(false, string.IsNullOrWhiteSpace(message) ? "Command execution failed." : message)
        {
            SkillId = skillId,
            Status = status,
            ErrorCode = errorCode,
            DurationMilliseconds = durationMilliseconds
        };
}
