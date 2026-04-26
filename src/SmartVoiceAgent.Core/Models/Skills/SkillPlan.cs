using System.Text.Json;

namespace SmartVoiceAgent.Core.Models.Skills;

public sealed class SkillPlan
{
    public string SkillId { get; set; } = string.Empty;

    public Dictionary<string, JsonElement> Arguments { get; set; } = [];

    public double Confidence { get; set; }

    public bool RequiresConfirmation { get; set; }

    public string Reasoning { get; set; } = string.Empty;

    public static SkillPlan FromObject(string skillId, object? arguments)
    {
        var plan = new SkillPlan { SkillId = skillId };
        if (arguments is null)
        {
            return plan;
        }

        var element = JsonSerializer.SerializeToElement(arguments);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return plan;
        }

        plan.Arguments = element
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return plan;
    }
}
