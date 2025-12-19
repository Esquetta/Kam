using Microsoft.Agents.AI;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Agent.Thread;
/// <summary>
/// Custom thread implementation for SystemAgent
/// </summary>
public sealed class SystemAgentThread : InMemoryAgentThread
{
    public SystemAgentThread() : base() { }

    public SystemAgentThread(
        JsonElement serializedThreadState,
        JsonSerializerOptions? jsonSerializerOptions = null)
        : base(serializedThreadState, jsonSerializerOptions) { }
}