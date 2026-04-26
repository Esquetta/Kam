namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillPolicyState
{
    public string SkillId { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public bool ReviewRequired { get; set; }

    public List<SkillPermission> GrantedPermissions { get; set; } = [];
}
