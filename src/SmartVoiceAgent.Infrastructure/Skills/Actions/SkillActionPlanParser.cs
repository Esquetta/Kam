using System.Text.Json;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Actions;

public static class SkillActionPlanParser
{
    private static readonly HashSet<string> SupportedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        SkillActionTypes.Respond,
        SkillActionTypes.OpenApp,
        SkillActionTypes.FocusWindow,
        SkillActionTypes.Click,
        SkillActionTypes.TypeText,
        SkillActionTypes.Hotkey,
        SkillActionTypes.ClipboardSet,
        SkillActionTypes.ClipboardGet,
        SkillActionTypes.ReadScreen
    };

    public static SkillActionPlanParseResult ParseStrict(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return SkillActionPlanParseResult.Invalid("The model returned an empty action plan.");
        }

        var trimmed = response.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return SkillActionPlanParseResult.Invalid("The model must return a single JSON object with no surrounding text.");
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return SkillActionPlanParseResult.Invalid("The model must return a single JSON object.");
            }

            var plan = new SkillActionPlan
            {
                Message = ReadString(root, "message"),
                RequiresConfirmation = ReadBoolean(root, "requiresConfirmation")
                    || ReadBoolean(root, "requires_confirmation")
            };

            var actionsElement = TryGetProperty(root, "actions", out var actions)
                ? actions
                : TryGetProperty(root, "steps", out var steps)
                    ? steps
                    : default;

            if (actionsElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                return SkillActionPlanParseResult.Valid(plan);
            }

            if (actionsElement.ValueKind != JsonValueKind.Array)
            {
                return SkillActionPlanParseResult.Invalid("'actions' must be a JSON array.");
            }

            foreach (var actionElement in actionsElement.EnumerateArray())
            {
                if (actionElement.ValueKind != JsonValueKind.Object)
                {
                    return SkillActionPlanParseResult.Invalid("Each action must be a JSON object.");
                }

                var action = ReadAction(actionElement);
                if (string.IsNullOrWhiteSpace(action.Type))
                {
                    return SkillActionPlanParseResult.Invalid("Each action must include a 'type'.");
                }

                if (!SupportedActions.Contains(action.Type))
                {
                    return SkillActionPlanParseResult.Invalid($"Unsupported action '{action.Type}'.");
                }

                plan.Actions.Add(action);
            }

            return SkillActionPlanParseResult.Valid(plan);
        }
        catch (JsonException ex)
        {
            return SkillActionPlanParseResult.Invalid($"The model returned invalid JSON: {ex.Message}");
        }
    }

    public static IReadOnlyCollection<string> GetSupportedActionTypes() => SupportedActions.ToArray();

    private static SkillActionStep ReadAction(JsonElement element)
    {
        return new SkillActionStep
        {
            Type = ReadString(element, "type"),
            Target = ReadString(element, "target"),
            ApplicationName = ReadString(element, "applicationName", "application", "app"),
            WindowTitle = ReadString(element, "windowTitle", "title"),
            Text = ReadString(element, "text", "content", "value"),
            Keys = ReadStringArray(element, "keys", "hotkey"),
            X = ReadInt(element, "x"),
            Y = ReadInt(element, "y"),
            TimeoutMilliseconds = ReadInt(element, "timeoutMilliseconds", "timeoutMs") ?? 0
        };
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool ReadBoolean(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out var property)
            && property.ValueKind == JsonValueKind.True;
    }

    private static int? ReadInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
            {
                return value;
            }

            if (property.ValueKind == JsonValueKind.String
                && int.TryParse(property.GetString(), out value))
            {
                return value;
            }
        }

        return null;
    }

    private static List<string> ReadStringArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString()?
                    .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToList() ?? [];
            }
        }

        return [];
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement property)
    {
        foreach (var current in element.EnumerateObject())
        {
            if (current.NameEquals(name)
                || current.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                property = current.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}

public sealed record SkillActionPlanParseResult(
    bool IsValid,
    SkillActionPlan? Plan,
    string ErrorMessage)
{
    public static SkillActionPlanParseResult Valid(SkillActionPlan plan) =>
        new(true, plan, string.Empty);

    public static SkillActionPlanParseResult Invalid(string errorMessage) =>
        new(false, null, errorMessage);
}
