using BenchmarkDotNet.Attributes;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Benchmarks;

/// <summary>
/// Benchmarks for CommandInputService performance
/// </summary>
[MemoryDiagnoser]
public class CommandInputBenchmark
{
    private ICommandInputService _commandInput = null!;
    private const int CommandCount = 1000;
    private readonly List<string> _commands = new();

    [GlobalSetup]
    public void Setup()
    {
        _commandInput = new CommandInputService();

        // Pre-generate commands
        for (int i = 0; i < CommandCount; i++)
        {
            _commands.Add($"command {i}: {Guid.NewGuid()}");
        }
    }

    [Benchmark]
    public void SubmitCommand()
    {
        _commandInput.SubmitCommand("test command");
    }

    [Benchmark]
    public async Task ReadCommandAsync()
    {
        _commandInput.SubmitCommand("test");
        await _commandInput.ReadCommandAsync();
    }

    [Benchmark]
    public void SubmitMultipleCommands()
    {
        foreach (var cmd in _commands)
        {
            _commandInput.SubmitCommand(cmd);
        }
    }

    [Benchmark]
    public async Task ProducerConsumerPattern()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tasks = new List<Task>();

        // Producer
        tasks.Add(Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                _commandInput.SubmitCommand($"cmd-{i}");
            }
        }));

        // Consumer
        tasks.Add(Task.Run(async () =>
        {
            int count = 0;
            while (count < 100 && !cts.Token.IsCancellationRequested)
            {
                await _commandInput.ReadCommandAsync(cts.Token);
                count++;
            }
        }));

        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public void PublishResult()
    {
        _commandInput.PublishResult("test-cmd", "test result", true);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        (_commandInput as IDisposable)?.Dispose();
        _commandInput = new CommandInputService();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        (_commandInput as IDisposable)?.Dispose();
    }
}
