using System.Text.Json;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Planning;

public static class SkillPlanParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static SkillPlanParseResult Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return SkillPlanParseResult.Failure("Response must contain valid JSON.");
        }

        var json = ExtractJsonObject(RemoveMarkdownFence(response));
        if (json is null)
        {
            return SkillPlanParseResult.Failure("Response must contain valid JSON.");
        }

        try
        {
            var plan = JsonSerializer.Deserialize<SkillPlan>(json, JsonOptions);
            if (plan is null || string.IsNullOrWhiteSpace(plan.SkillId))
            {
                return SkillPlanParseResult.Failure("Skill plan JSON must include a skillId.");
            }

            plan.Arguments ??= [];
            return SkillPlanParseResult.Success(plan);
        }
        catch (JsonException)
        {
            return SkillPlanParseResult.Failure("Response must contain valid JSON.");
        }
    }

    private static string RemoveMarkdownFence(string response)
    {
        var trimmed = response.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstLineEnd = trimmed.IndexOf('\n');
        var lastFenceStart = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstLineEnd < 0 || lastFenceStart <= firstLineEnd)
        {
            return trimmed;
        }

        return trimmed[(firstLineEnd + 1)..lastFenceStart].Trim();
    }

    private static string? ExtractJsonObject(string value)
    {
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return value[start..(end + 1)];
    }
}
