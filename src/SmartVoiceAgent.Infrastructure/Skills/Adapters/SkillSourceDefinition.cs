namespace SmartVoiceAgent.Infrastructure.Skills.Adapters;

public sealed class SkillSourceDefinition
{
    public string Id { get; set; } = string.Empty;

    public SkillSourceKind Kind { get; set; }

    public string Location { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public List<ExternalSkillDefinition> Skills { get; set; } = [];
}
