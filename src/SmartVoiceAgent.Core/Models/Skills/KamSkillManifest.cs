namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class KamSkillManifest
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string ExecutorType { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public bool ReviewRequired { get; set; }

    public string Checksum { get; set; } = string.Empty;

    public string InstalledFrom { get; set; } = string.Empty;

    public DateTimeOffset InstalledAt { get; set; }

    public SkillRiskLevel RiskLevel { get; set; } = SkillRiskLevel.Low;

    public List<SkillPermission> Permissions { get; set; } = [];

    public List<SkillPermission> GrantedPermissions { get; set; } = [];

    public List<SkillArgumentDefinition> Arguments { get; set; } = [];

    public Dictionary<string, string> RuntimeOptions { get; set; } = [];

    public int TimeoutMilliseconds { get; set; } = 30000;
}
