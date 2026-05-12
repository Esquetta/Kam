namespace SmartVoiceAgent.Core.Models.Agents;

public sealed record RuntimeAgentResult(
    string AgentName,
    string Role,
    string Response,
    string ModelId);
