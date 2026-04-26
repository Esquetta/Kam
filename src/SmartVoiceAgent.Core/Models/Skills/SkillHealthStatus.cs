namespace SmartVoiceAgent.Core.Models.Skills;

public enum SkillHealthStatus
{
    Healthy = 0,
    Disabled = 1,
    MissingExecutor = 2,
    ReviewRequired = 3,
    PermissionDenied = 4
}
