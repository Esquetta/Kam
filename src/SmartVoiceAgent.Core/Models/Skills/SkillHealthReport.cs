namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillHealthReport
{
    public string SkillId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string ExecutorType { get; set; } = string.Empty;

    public string Checksum { get; set; } = string.Empty;

    public string InstalledFrom { get; set; } = string.Empty;

    public DateTimeOffset InstalledAt { get; set; }

    public SkillRiskLevel RiskLevel { get; set; } = SkillRiskLevel.Low;

    public SkillHealthStatus Status { get; set; } = SkillHealthStatus.Healthy;

    public string Details { get; set; } = string.Empty;

    public IReadOnlyCollection<SkillPermission> RequiredPermissions { get; set; } = [];

    public IReadOnlyCollection<SkillPermission> GrantedPermissions { get; set; } = [];

    public IReadOnlyCollection<SkillPermission> MissingPermissions { get; set; } = [];

    public IReadOnlyDictionary<string, string> RuntimeOptions { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? LastRunAt { get; set; }

    public SkillExecutionStatus? LastRunStatus { get; set; }

    public string LastRunMessage { get; set; } = string.Empty;

    public string LastRunErrorCode { get; set; } = string.Empty;

    public long LastRunDurationMilliseconds { get; set; }
}
