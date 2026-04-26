using System.Text.Json;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Execution;

internal static class SkillArgumentValidator
{
    public static string? Validate(KamSkillManifest manifest, SkillPlan plan)
    {
        foreach (var argument in manifest.Arguments)
        {
            if (string.IsNullOrWhiteSpace(argument.Name))
            {
                continue;
            }

            var valueFound = TryGetArgument(plan, argument.Name, out var value);
            if (!valueFound || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                if (argument.Required)
                {
                    return $"Argument '{argument.Name}' is required.";
                }

                continue;
            }

            if (argument.Required
                && argument.Type == SkillArgumentType.String
                && value.ValueKind == JsonValueKind.String
                && string.IsNullOrWhiteSpace(value.GetString()))
            {
                return $"Argument '{argument.Name}' is required.";
            }

            if (!MatchesType(value, argument.Type))
            {
                return $"Argument '{argument.Name}' must be {argument.Type.ToString().ToLowerInvariant()}.";
            }
        }

        return null;
    }

    private static bool TryGetArgument(SkillPlan plan, string name, out JsonElement value)
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

    private static bool MatchesType(JsonElement value, SkillArgumentType type)
    {
        return type switch
        {
            SkillArgumentType.Any => true,
            SkillArgumentType.String => value.ValueKind == JsonValueKind.String,
            SkillArgumentType.Number => value.ValueKind == JsonValueKind.Number,
            SkillArgumentType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SkillArgumentType.Object => value.ValueKind == JsonValueKind.Object,
            SkillArgumentType.Array => value.ValueKind == JsonValueKind.Array,
            _ => false
        };
    }
}
