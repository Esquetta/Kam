namespace SmartVoiceAgent.Core.Models.Agents;

public sealed record RuntimeAgentReadOnlyToolRequest(
    string Tool,
    string? Path = null,
    string? Query = null);
