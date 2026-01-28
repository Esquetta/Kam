using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Agent;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Benchmarks;

/// <summary>
/// Memory allocation benchmarks for critical components
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90, baseline: true)]
public class MemoryAllocationBenchmark
{
    private ConversationContextManager _contextManager = null!;
    private ICommandInputService _commandInput = null!;
    private List<IDisposable> _disposables = new();

    [GlobalSetup]
    public void Setup()
    {
        _contextManager = new ConversationContextManager(logger: null);
        _commandInput = new CommandInputService();
        _disposables.Add(_contextManager);
        _disposables.Add((IDisposable)_commandInput);
    }

    /// <summary>
    /// Measures allocation when creating conversation contexts
    /// </summary>
    [Benchmark]
    public void ConversationContextAllocation()
    {
        for (int i = 0; i < 100; i++)
        {
            _contextManager.StartConversation(Guid.NewGuid().ToString(), $"Message {i}");
        }
    }

    /// <summary>
    /// Measures allocation during context updates
    /// </summary>
    [Benchmark]
    public void ContextUpdateAllocation()
    {
        for (int i = 0; i < 100; i++)
        {
            _contextManager.UpdateContext("type", $"input {i}", $"result {i}");
        }
    }

    /// <summary>
    /// Measures allocation for command submissions
    /// </summary>
    [Benchmark]
    public void CommandSubmissionAllocation()
    {
        for (int i = 0; i < 1000; i++)
        {
            _commandInput.SubmitCommand($"command {i}");
        }
    }

    /// <summary>
    /// Measures string allocations in log entries
    /// </summary>
    [Benchmark]
    public void LogEntryStringAllocation()
    {
        var logs = new List<string>();
        for (int i = 0; i < 100; i++)
        {
            logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Log message number {i} with some additional context data");
        }
    }

    /// <summary>
    /// Measures array/list allocations
    /// </summary>
    [Benchmark]
    public void CollectionAllocation()
    {
        var list = new List<string>(100);
        for (int i = 0; i < 100; i++)
        {
            list.Add(Guid.NewGuid().ToString());
        }
    }

    /// <summary>
    /// Measures cleanup efficiency
    /// </summary>
    [Benchmark]
    public void CleanupEfficiency()
    {
        // Create many contexts
        for (int i = 0; i < 100; i++)
        {
            _contextManager.StartConversation(Guid.NewGuid().ToString(), "test");
            _contextManager.SetApplicationState($"app-{i}", true);
        }

        // Cleanup
        _contextManager.CleanupOldData();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        // Recreate services to reset state
        foreach (var d in _disposables)
        {
            d.Dispose();
        }
        _disposables.Clear();

        _contextManager = new ConversationContextManager(logger: null);
        _commandInput = new CommandInputService();
        _disposables.Add(_contextManager);
        _disposables.Add((IDisposable)_commandInput);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        foreach (var d in _disposables)
        {
            d.Dispose();
        }
        _disposables.Clear();
    }
}
