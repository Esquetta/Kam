using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Enums.Agent;

namespace SmartVoiceAgent.Core.Dtos.Agent;

public record RoutingDecision
{
    public List<string> TargetAgents { get; init; } = new();
    public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.Sequential;
    public string Reasoning { get; init; } = string.Empty;
}