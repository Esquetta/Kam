using BenchmarkDotNet.Attributes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Agent.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Functions;
using SmartVoiceAgent.Infrastructure.Agent.Tools;
using System.ClientModel;

namespace SmartVoiceAgent.Benchmarks;

/// <summary>
/// Benchmarks for AI Agent creation and initialization performance
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class AgentCreationBenchmark
{
    private ServiceProvider _serviceProvider = null!;
    private IChatClient _chatClient = null!;
    private IAgentFactory _agentFactory = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.ClearProviders());

        // Add fake chat client for benchmarking
        services.AddSingleton<IChatClient, FakeChatClient>();

        // Add agent factory
        services.AddSingleton<IAgentFactory, AgentFactory>();

        // Add tool services
        services.AddSingleton<SystemAgentTools>();
        services.AddSingleton<TaskAgentTools>();
        services.AddSingleton<WebSearchAgentTools>();

        _serviceProvider = services.BuildServiceProvider();
        _chatClient = _serviceProvider.GetRequiredService<IChatClient>();
        _agentFactory = _serviceProvider.GetRequiredService<IAgentFactory>();
    }

    [Benchmark]
    public AIAgent CreateSystemAgent()
    {
        return _agentFactory.CreateSystemAgent();
    }

    [Benchmark]
    public async Task<AIAgent> CreateTaskAgentAsync()
    {
        return await _agentFactory.CreateTaskAgentAsync();
    }

    [Benchmark]
    public AIAgent CreateResearchAgent()
    {
        return _agentFactory.CreateResearchAgent();
    }

    [Benchmark]
    public AIAgent CreateCoordinatorAgent()
    {
        return _agentFactory.CreateCoordinatorAgent();
    }

    [Benchmark]
    public void CreateAllAgents()
    {
        _ = _agentFactory.CreateSystemAgent();
        _ = _agentFactory.CreateResearchAgent();
        _ = _agentFactory.CreateCoordinatorAgent();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }
}

/// <summary>
/// Fake chat client for benchmarking without external dependencies
/// </summary>
public class FakeChatClient : IChatClient, IDisposable
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, "Benchmark response")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return AsyncEnumerable.Empty<ChatResponseUpdate>();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}
