namespace SmartVoiceAgent.Core.Models.Agents;

/// <summary>
/// Something that happened during an agent turn, streamed to the UI as it happens.
/// </summary>
public abstract record AgentEvent(string SessionId);

/// <summary>A chunk of assistant text.</summary>
public sealed record AgentTextDelta(string SessionId, string Text) : AgentEvent(SessionId);

/// <summary>The model asked to call a tool.</summary>
public sealed record AgentToolCallStarted(
    string SessionId,
    string CallId,
    string ToolName,
    string DisplayName,
    string ArgumentsJson,
    ToolRisk Risk) : AgentEvent(SessionId);

/// <summary>A tool call finished, failed, or was denied.</summary>
public sealed record AgentToolCallCompleted(
    string SessionId,
    string CallId,
    string ToolName,
    bool Success,
    string Summary) : AgentEvent(SessionId);

/// <summary>
/// The runtime is waiting for the user to approve this exact call.
/// Answer it with <c>IAgentRuntime.ResolveApproval</c>.
/// </summary>
/// <param name="AlwaysAllowRule">The rule "Always allow" saves, such as <c>shell_run(git status:*)</c>.</param>
public sealed record AgentApprovalRequested(
    string SessionId,
    string RequestId,
    string CallId,
    string ToolName,
    string DisplayName,
    string ArgumentsJson,
    ToolRisk Risk,
    string? AlwaysAllowRule = null) : AgentEvent(SessionId);

/// <summary>
/// Earlier messages were summarized so the conversation fits the context budget.
/// The thread still holds them; the model now sees the summary instead.
/// </summary>
public sealed record AgentContextCompacted(
    string SessionId,
    int SummarizedMessageCount,
    int TokensBefore,
    int TokensAfter) : AgentEvent(SessionId);

/// <summary>
/// A model request failed with a temporary error and is sent again after <paramref name="Delay"/>.
/// <paramref name="Attempt"/> counts retries of that request from 1 to <paramref name="MaxRetries"/>.
/// When <paramref name="PartialOutputDiscarded"/> is true, the text already streamed for this step
/// is dropped: the request starts over and its text arrives again.
/// </summary>
/// <param name="Reason">Short English cause, such as "rate limited (HTTP 429)".</param>
public sealed record AgentModelRetryScheduled(
    string SessionId,
    int Attempt,
    int MaxRetries,
    TimeSpan Delay,
    string Reason,
    bool PartialOutputDiscarded) : AgentEvent(SessionId);

/// <summary>
/// The turn ended with a final answer. <paramref name="ContextTokens"/> is the input size of the
/// turn's last model request, as the provider reported it: how full the context was at the end.
/// 0 when the provider reported no usage.
/// </summary>
public sealed record AgentTurnCompleted(
    string SessionId,
    string FinalText,
    int ToolCallCount,
    long InputTokens,
    long OutputTokens,
    long ContextTokens = 0) : AgentEvent(SessionId);

/// <summary>The turn stopped because of an error or a limit.</summary>
public sealed record AgentTurnFailed(string SessionId, string Message) : AgentEvent(SessionId);
