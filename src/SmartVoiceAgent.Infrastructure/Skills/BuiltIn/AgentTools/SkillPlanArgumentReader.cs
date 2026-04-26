using System.Globalization;
using System.Text.Json;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

internal static class SkillPlanArgumentReader
{
    public static string GetString(SkillPlan plan, string name, string defaultValue = "")
    {
        if (!TryGet(plan, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : value.ToString();
    }

    public static bool GetBool(SkillPlan plan, string name, bool defaultValue = false)
    {
        if (!TryGet(plan, name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }

    public static int GetInt(SkillPlan plan, string name, int defaultValue = 0)
    {
        if (!TryGet(plan, name, out var value))
        {
            return defaultValue;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
    }

    private static bool TryGet(SkillPlan plan, string name, out JsonElement value)
    {
        if (plan.Arguments.TryGetValue(name, out value))
        {
            return true;
        }

        foreach (var pair in plan.Arguments)
        {
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
