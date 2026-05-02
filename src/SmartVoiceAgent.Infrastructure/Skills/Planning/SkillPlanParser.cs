using System.Text.Json;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Core.Security;

namespace SmartVoiceAgent.Infrastructure.Skills.Planning;

public static class SkillPlanParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static SkillPlanParseResult Parse(string response)
    {
        var sanitizedRawResponse = SanitizeRawResponse(response);
        if (string.IsNullOrWhiteSpace(response))
        {
            return SkillPlanParseResult.Failure(
                "Response must contain valid JSON.",
                sanitizedRawResponse);
        }

        var json = ExtractJsonObject(RemoveMarkdownFence(response));
        if (json is null)
        {
            return SkillPlanParseResult.Failure(
                "Response must contain valid JSON.",
                sanitizedRawResponse);
        }

        try
        {
            return DeserializePlan(json, sanitizedRawResponse);
        }
        catch (JsonException)
        {
            return SkillPlanParseResult.Failure(
                "Response must contain valid JSON.",
                sanitizedRawResponse);
        }
    }

    public static SkillPlanParseResult ParseStrictJsonObject(string response)
    {
        var sanitizedRawResponse = SanitizeRawResponse(response);
        if (string.IsNullOrWhiteSpace(response))
        {
            return SkillPlanParseResult.Failure(
                "Planner response must be a single JSON object.",
                sanitizedRawResponse);
        }

        var trimmed = response.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
        {
            return SkillPlanParseResult.Failure(
                "Planner response must be a single JSON object.",
                sanitizedRawResponse);
        }

        try
        {
            return DeserializePlan(trimmed, sanitizedRawResponse);
        }
        catch (JsonException)
        {
            return SkillPlanParseResult.Failure(
                "Response must contain valid JSON.",
                sanitizedRawResponse);
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

    private static SkillPlanParseResult DeserializePlan(string json, string sanitizedRawResponse)
    {
        var plan = JsonSerializer.Deserialize<SkillPlan>(json, JsonOptions);
        if (plan is null || string.IsNullOrWhiteSpace(plan.SkillId))
        {
            return SkillPlanParseResult.Failure(
                "Skill plan JSON must include a skillId.",
                sanitizedRawResponse);
        }

        plan.Arguments ??= [];
        return SkillPlanParseResult.Success(plan, sanitizedRawResponse);
    }

    private static string SanitizeRawResponse(string? response)
    {
        return SecretRedactor.Redact(response);
    }
}
