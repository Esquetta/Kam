using Microsoft.Agents.AI;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Agent.Thread;
/// <summary>
/// Custom thread implementation for ResearchAgent
/// </summary>
public sealed class ResearchAgentThread : InMemoryAgentThread
{
    public ResearchAgentThread() : base() { }

    public ResearchAgentThread(
        JsonElement serializedThreadState,
        JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions) { }
}
