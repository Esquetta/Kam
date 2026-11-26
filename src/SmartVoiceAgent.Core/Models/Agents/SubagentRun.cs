using System.Text.Json.Serialization;

namespace SmartVoiceAgent.Core.Models.Agents;

/// <summary>
/// Where a subagent run stands.
/// </summary>
public enum SubagentRunStatus
{
    /// <summary>The subagent is working.</summary>
    Running = 0,

    /// <summary>The subagent finished with a report.</summary>
    Completed = 1,

    /// <summary>The subagent stopped because of an error or its step limit.</summary>
    Failed = 2,

    /// <summary>The user or the parent turn stopped the subagent.</summary>
    Cancelled = 3
}

/// <summary>
/// Where one tool call of a subagent stands.
/// </summary>
public enum SubagentStepStatus
{
    /// <summary>The call is running.</summary>
    Running = 0,

    /// <summary>The call waits for the user's approval.</summary>
    AwaitingApproval = 1,

    /// <summary>The call finished.</summary>
    Succeeded = 2,

    /// <summary>The call failed, was declined or was blocked.</summary>
    Failed = 3
}

/// <summary>
/// One tool call a subagent made.
/// </summary>
/// <param name="CallId">The model's call id, unique within the run.</param>
/// <param name="ToolName">The model-facing tool name.</param>
/// <param name="DisplayName">The tool's human-readable name.</param>
/// <param name="ArgumentsJson">The call's arguments, shortened for storage.</param>
/// <param name="Risk">What the tool can change.</param>
/// <param name="Status">Where the call stands.</param>
/// <param name="StartedAt">When the call was announced.</param>
/// <param name="CompletedAt">When it finished, or null while it runs.</param>
/// <param name="Summary">The first line of the result, or the reason it did not run.</param>
public sealed record SubagentStep(
    string CallId,
    string ToolName,
    string DisplayName,
    string ArgumentsJson,
    ToolRisk Risk,
    SubagentStepStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    string Summary = "");

/// <summary>
/// A subagent the main agent started from a task with the <c>task</c> tool: its brief, its tool calls and its report.
/// </summary>
/// <param name="RunId">The run id.</param>
/// <param name="ParentSessionId">The task (session) whose turn started the subagent.</param>
/// <param name="ParentCallId">The <c>task</c> call that started it.</param>
/// <param name="AgentType">The subagent type, such as "explorer".</param>
/// <param name="Description">The short label the main agent gave the job.</param>
/// <param name="Prompt">The brief the subagent received.</param>
/// <param name="Status">Where the run stands.</param>
/// <param name="StartedAt">When the run started.</param>
/// <param name="CompletedAt">When it ended, or null while it runs.</param>
/// <param name="ModelId">The model it ran on, or null for the model chosen in Settings.</param>
/// <param name="Steps">Its tool calls in the order they were announced.</param>
/// <param name="InputTokens">Input tokens the provider reported for the run.</param>
/// <param name="OutputTokens">Output tokens the provider reported for the run.</param>
/// <param name="Result">The report the main agent received, or null while it runs.</param>
/// <param name="Error">Why it failed or stopped, or null.</param>
public sealed record SubagentRun(
    string RunId,
    string ParentSessionId,
    string ParentCallId,
    string AgentType,
    string Description,
    string Prompt,
    SubagentRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    string? ModelId = null,
    IReadOnlyList<SubagentStep>? Steps = null,
    long InputTokens = 0,
    long OutputTokens = 0,
    string? Result = null,
    string? Error = null)
{
    /// <summary>Gets the tool calls, never null.</summary>
    [JsonIgnore]
    public IReadOnlyList<SubagentStep> StepList => Steps ?? [];

    /// <summary>Gets whether a tool call of the run waits for the user.</summary>
    [JsonIgnore]
    public bool IsWaitingForApproval =>
        Status == SubagentRunStatus.Running && StepList.Any(step => step.Status == SubagentStepStatus.AwaitingApproval);
}
