namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillAuditRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string SkillId { get; set; } = string.Empty;

    public string ExecutorType { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    public string UserInput { get; set; } = string.Empty;

    public string ActionPlanJson { get; set; } = string.Empty;

    public List<string> ActionTypes { get; set; } = [];

    public List<SkillPermission> RequiredPermissions { get; set; } = [];

    public List<SkillPermission> MissingPermissions { get; set; } = [];

    public SkillExecutionStatus Status { get; set; } = SkillExecutionStatus.Failed;

    public string ResultMessage { get; set; } = string.Empty;

    public string ErrorCode { get; set; } = string.Empty;

    public long DurationMilliseconds { get; set; }
}
