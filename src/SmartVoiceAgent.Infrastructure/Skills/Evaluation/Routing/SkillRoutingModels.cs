using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Skills.Evaluation.Routing;

/// <summary>
/// How the model is asked to pick a skill.
/// </summary>
public enum SkillRoutingMode
{
    /// <summary>The model returns a JSON plan (<c>ModelSkillPlannerService</c>).</summary>
    JsonPlan,

    /// <summary>The model calls one of the skills offered as native tools, as in the agent loop.</summary>
    ToolCall
}

/// <summary>
/// One request and the skill it should be routed to.
/// </summary>
public sealed class SkillRoutingCase
{
    /// <summary>Stable id, shared by the English and Turkish wording of the same task.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Language of <see cref="Request"/> ("en" or "tr").</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>What the user says.</summary>
    public string Request { get; init; } = string.Empty;

    /// <summary>Skills that do the job; any of them counts as correct.</summary>
    public IReadOnlyList<string> ExpectedSkills { get; init; } = [];

    /// <summary>
    /// Argument name to the values that count as right, separated by <c>|</c>. A value must contain one of
    /// them, or equal it when the alternative starts with <c>=</c>. Case and path separators are ignored.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExpectedArguments { get; init; } = new Dictionary<string, string>();

    /// <summary>Reads cases from a JSON array.</summary>
    /// <param name="json">The file contents.</param>
    public static IReadOnlyList<SkillRoutingCase> ParseAll(string json)
    {
        var cases = JsonSerializer.Deserialize<List<SkillRoutingCase>>(json, SkillRoutingJson.Options) ?? [];
        foreach (var testCase in cases)
        {
            if (string.IsNullOrWhiteSpace(testCase.Id)
                || string.IsNullOrWhiteSpace(testCase.Request)
                || testCase.ExpectedSkills.Count == 0)
            {
                throw new InvalidDataException(
                    $"Case '{testCase.Id}' needs an id, a request and at least one expected skill.");
            }
        }

        return cases;
    }
}

/// <summary>
/// The outcome of one case for one model in one mode.
/// </summary>
public sealed class SkillRoutingAttempt
{
    public string CaseId { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public SkillRoutingMode Mode { get; init; }

    /// <summary>The skill the model picked, or empty when it picked none.</summary>
    public string ChosenSkill { get; init; } = string.Empty;

    /// <summary>The arguments the model gave, as JSON.</summary>
    public string Arguments { get; init; } = "{}";

    /// <summary>Tool calls made across all steps (tool mode only).</summary>
    public int ToolCallCount { get; init; }

    /// <summary>Model calls used. Always 1 for a JSON plan.</summary>
    public int Steps { get; init; }

    /// <summary>The first thing the model did was the right skill.</summary>
    public bool FirstCallCorrect { get; init; }

    /// <summary>A known skill with arguments that pass Kam's argument validation.</summary>
    public bool Valid { get; init; }

    public bool SkillCorrect { get; init; }

    /// <summary>Right skill, valid arguments, and every expected argument matched.</summary>
    public bool ArgumentsCorrect { get; init; }

    /// <summary>Why the attempt is not valid, when it is not.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>The provider call failed after retries; the attempt is left out of the rates.</summary>
    public bool TransportError { get; init; }

    /// <summary>Wall time of all model calls in the attempt.</summary>
    public long DurationMilliseconds { get; init; }

    /// <summary>Tokens summed over all model calls in the attempt.</summary>
    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }
}

/// <summary>
/// JSON settings for cases and attempts: camelCase names, enums as text.
/// </summary>
public static class SkillRoutingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
