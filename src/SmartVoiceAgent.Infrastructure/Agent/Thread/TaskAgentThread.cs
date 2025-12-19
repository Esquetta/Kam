using Microsoft.Agents.AI;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Agent.Thread;
/// <summary>
/// Custom thread implementation for TaskAgent
/// </summary>
public sealed class TaskAgentThread : InMemoryAgentThread
{
    public TaskAgentThread() : base() { }

    public TaskAgentThread(
        JsonElement serializedThreadState,
        JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions) { }
}