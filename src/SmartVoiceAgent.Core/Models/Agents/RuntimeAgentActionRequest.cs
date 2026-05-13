namespace SmartVoiceAgent.Core.Models.Agents;

public sealed record RuntimeAgentActionRequest(
    string Action,
    string? FilePath = null,
    string? OldText = null,
    string? NewText = null,
    int ExpectedOccurrences = 1,
    string? Command = null);
