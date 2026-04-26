namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillArgumentDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public SkillArgumentType Type { get; set; } = SkillArgumentType.Any;

    public bool Required { get; set; }
}
