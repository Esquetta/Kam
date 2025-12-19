using Microsoft.Agents.AI;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Agent.Thread;
/// <summary>
/// Custom thread implementation for Coordinator
/// </summary>
public sealed class CoordinatorAgentThread : InMemoryAgentThread
{
    public CoordinatorAgentThread() : base() { }

    public CoordinatorAgentThread(
        JsonElement serializedThreadState,
        JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions) { }
}