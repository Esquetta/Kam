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
public sealed record AgentApprovalRequested(
    string SessionId,
    string RequestId,
    string CallId,
    string ToolName,
    string DisplayName,
    string ArgumentsJson,
    ToolRisk Risk) : AgentEvent(SessionId);

/// <summary>
/// Earlier messages were summarized so the conversation fits the context budget.
/// The thread still holds them; the model now sees the summary instead.
/// </summary>
public sealed record AgentContextCompacted(
    string SessionId,
    int SummarizedMessageCount,
    int TokensBefore,
    int TokensAfter) : AgentEvent(SessionId);

/// <summary>The turn ended with a final answer.</summary>
public sealed record AgentTurnCompleted(
    string SessionId,
    string FinalText,
    int ToolCallCount,
    long InputTokens,
    long OutputTokens) : AgentEvent(SessionId);

/// <summary>The turn stopped because of an error or a limit.</summary>
public sealed record AgentTurnFailed(string SessionId, string Message) : AgentEvent(SessionId);
