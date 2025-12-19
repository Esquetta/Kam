using Microsoft.Extensions.AI;

namespace SmartVoiceAgent.Core.Dtos.Agent;

public record AgentResponse
{
    public string AgentName { get; init; } = string.Empty;
    public ChatMessage Message { get; init; } = null!;
}