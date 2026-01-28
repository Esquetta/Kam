using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Infrastructure.Agent;

namespace SmartVoiceAgent.Benchmarks;

/// <summary>
/// Benchmarks for ConversationContextManager operations
/// </summary>
[MemoryDiagnoser]
public class ConversationContextBenchmark
{
    private ConversationContextManager _contextManager = null!;
    private const int ConversationCount = 100;
    private readonly List<string> _conversationIds = new();

    [GlobalSetup]
    public void Setup()
    {
        _contextManager = new ConversationContextManager(logger: null);

        // Pre-generate conversation IDs
        for (int i = 0; i < ConversationCount; i++)
        {
            _conversationIds.Add($"conv-{Guid.NewGuid()}");
        }
    }

    [Benchmark]
    public void StartConversation()
    {
        var id = Guid.NewGuid().ToString();
        _contextManager.StartConversation(id, "Test message");
    }

    [Benchmark]
    public void UpdateContext()
    {
        _contextManager.UpdateContext("command", "test input", "test result");
    }

    [Benchmark]
    public string GetRelevantContext()
    {
        return _contextManager.GetRelevantContext("music volume test");
    }

    [Benchmark]
    public void SetApplicationState()
    {
        var appName = $"App-{Guid.NewGuid()}";
        _contextManager.SetApplicationState(appName, true);
    }

    [Benchmark]
    public bool IsApplicationOpen()
    {
        return _contextManager.IsApplicationOpen("spotify");
    }

    [Benchmark]
    public void StartMultipleConversations()
    {
        foreach (var id in _conversationIds)
        {
            _contextManager.StartConversation(id, "Benchmark message");
        }
    }

    [Benchmark]
    public void CleanupOldData()
    {
        _contextManager.CleanupOldData();
    }

    [Benchmark]
    public void EndConversation()
    {
        var id = _conversationIds[0];
        _contextManager.EndConversation(id);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        // Create fresh manager for next iteration to avoid state buildup
        _contextManager = new ConversationContextManager(logger: null);
    }
}
