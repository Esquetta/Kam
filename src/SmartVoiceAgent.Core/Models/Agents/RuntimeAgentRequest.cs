namespace SmartVoiceAgent.Core.Models.Agents;

public sealed record RuntimeAgentRequest(
    string AgentName,
    string Role,
    string UserRequest);
