using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Enums.Agent;

namespace SmartVoiceAgent.Core.Dtos.Agent;

public record RoutingDecision
{
    public ChatMessage OriginalRequest { get; init; } = null!;
    public List<string> TargetAgents { get; init; } = new();
    public ExecutionMode ExecutionMode { get; init; }
}