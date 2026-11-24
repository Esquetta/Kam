using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartVoiceAgent.Infrastructure.Agent.Evaluation;

/// <summary>
/// A multi-step task for the agent: files to start from, the request, and checks on the result.
/// </summary>
public sealed class AgentTaskCase
{
    /// <summary>Stable id, shared by the English and Turkish wording of the same task.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Language of <see cref="Request"/> ("en" or "tr").</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>What the user asks. Paths are relative to the workspace folder the agent is told about.</summary>
    public string Request { get; init; } = string.Empty;

    /// <summary>Workspace-relative path to file contents, written before the run.</summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();

    /// <summary>Checks on the workspace and the final reply after the run. All must pass.</summary>
    public IReadOnlyList<AgentTaskCheck> Checks { get; init; } = [];

    /// <summary>Reads tasks from a JSON array.</summary>
    /// <param name="json">The file contents.</param>
    public static IReadOnlyList<AgentTaskCase> ParseAll(string json)
    {
        var cases = JsonSerializer.Deserialize<List<AgentTaskCase>>(json, AgentTaskJson.Options) ?? [];
        foreach (var testCase in cases)
        {
            if (string.IsNullOrWhiteSpace(testCase.Id)
                || string.IsNullOrWhiteSpace(testCase.Request)
                || testCase.Checks.Count == 0)
            {
                throw new InvalidDataException($"Task '{testCase.Id}' needs an id, a request and at least one check.");
            }
        }

        return cases;
    }
}

/// <summary>
/// What a check looks at.
/// </summary>
public enum AgentTaskCheckType
{
    /// <summary>The file exists.</summary>
    Exists,

    /// <summary>The file or folder does not exist.</summary>
    Missing,

    /// <summary>The file contains <see cref="AgentTaskCheck.Text"/>.</summary>
    Contains,

    /// <summary>The file does not contain <see cref="AgentTaskCheck.Text"/>.</summary>
    NotContains,

    /// <summary>The file's trimmed contents equal <see cref="AgentTaskCheck.Text"/>.</summary>
    Equals,

    /// <summary>The agent's final reply contains <see cref="AgentTaskCheck.Text"/>.</summary>
    AnswerContains
}

/// <summary>
/// One check on the result. Text comparisons ignore case, surrounding space and line endings.
/// </summary>
public sealed class AgentTaskCheck
{
    public AgentTaskCheckType Type { get; init; }

    /// <summary>Workspace-relative path; not used by <see cref="AgentTaskCheckType.AnswerContains"/>.</summary>
    public string Path { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    /// <inheritdoc />
    public override string ToString()
    {
        return Type == AgentTaskCheckType.AnswerContains
            ? $"answer contains \"{Text}\""
            : string.IsNullOrEmpty(Text) ? $"{Type} {Path}" : $"{Path} {Type} \"{Text}\"";
    }
}

/// <summary>
/// The outcome of one task for one model.
/// </summary>
public sealed class AgentTaskAttempt
{
    public string CaseId { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    /// <summary>Every check passed.</summary>
    public bool Passed { get; init; }

    /// <summary>Checks that failed, as text.</summary>
    public IReadOnlyList<string> FailedChecks { get; init; } = [];

    /// <summary>Tool calls the agent made.</summary>
    public int ToolCalls { get; init; }

    /// <summary>Tool calls that ran and reported an error.</summary>
    public int FailedToolCalls { get; init; }

    /// <summary>Tool calls the sandbox refused (tool not allowed, or a path outside the workspace).</summary>
    public int BlockedToolCalls { get; init; }

    /// <summary>The refused calls: tool name and arguments.</summary>
    public IReadOnlyList<string> Blocked { get; init; } = [];

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long DurationMilliseconds { get; init; }

    /// <summary>Why the turn did not finish, when it did not.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>
    /// The final reply is in the request's language: a Turkish reply has Turkish letters
    /// (ç, ğ, ı, ö, ş, ü), an English one has none. A heuristic, good enough for short replies.
    /// </summary>
    public bool ReplyInRequestLanguage { get; init; }

    /// <summary>The start of the agent's final reply.</summary>
    public string FinalText { get; init; } = string.Empty;

    /// <summary>Tools called, in order.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];
}

/// <summary>
/// JSON settings for tasks and attempts: camelCase names, enums as text.
/// </summary>
public static class AgentTaskJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
