namespace SmartVoiceAgent.Core.Dtos.Agent;

public record AgentExecutionUpdate
{
    public string AgentName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool IsComplete { get; init; }
}