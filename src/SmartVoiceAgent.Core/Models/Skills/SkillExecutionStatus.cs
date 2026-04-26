namespace SmartVoiceAgent.Core.Models.Skills;

public enum SkillExecutionStatus
{
    Succeeded = 0,
    Failed = 1,
    ValidationFailed = 2,
    TimedOut = 3,
    SkillNotFound = 4,
    Disabled = 5,
    ExecutorNotFound = 6,
    Cancelled = 7,
    ReviewRequired = 8,
    PermissionDenied = 9
}
