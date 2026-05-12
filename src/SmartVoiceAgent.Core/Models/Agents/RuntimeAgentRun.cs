namespace SmartVoiceAgent.Core.Models.Agents;

public sealed record RuntimeAgentRun(
    string RunId,
    string AgentName,
    string Role,
    string Task,
    string ModelId,
    RuntimeAgentRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    string? LastMessage = null,
    string? Response = null,
    string? ErrorMessage = null,
    IReadOnlyList<RuntimeAgentToolObservation>? ToolObservations = null);
