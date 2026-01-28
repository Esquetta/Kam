using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.UiLog;

namespace SmartVoiceAgent.Benchmarks;

/// <summary>
/// Benchmarks for UI logging performance
/// </summary>
[MemoryDiagnoser]
public class UiLogBenchmark
{
    private IUiLogService _uiLogService = null!;
    private UiLogLoggerProvider _loggerProvider = null!;
    private Microsoft.Extensions.Logging.ILogger _logger = null!;
    private const int LogCount = 1000;

    [GlobalSetup]
    public void Setup()
    {
        _uiLogService = new ConsoleUiLogService();
        _loggerProvider = new UiLogLoggerProvider(_uiLogService);
        _logger = _loggerProvider.CreateLogger("Benchmark");
    }

    [Benchmark]
    public void LogInfo()
    {
        _logger.LogInformation("Test log message: {Value}", Guid.NewGuid());
    }

    [Benchmark]
    public void LogWarning()
    {
        _logger.LogWarning("Warning message: {Value}", Guid.NewGuid());
    }

    [Benchmark]
    public void LogError()
    {
        _logger.LogError("Error message: {Value}", Guid.NewGuid());
    }

    [Benchmark]
    public void LogDebug()
    {
        _logger.LogDebug("Debug message: {Value}", Guid.NewGuid());
    }

    [Benchmark]
    public void LogMultipleMessages()
    {
        for (int i = 0; i < 100; i++)
        {
            _logger.LogInformation("Message {Index}: {Guid}", i, Guid.NewGuid());
        }
    }

    [Benchmark]
    public void LogWithException()
    {
        var ex = new InvalidOperationException("Test exception");
        _logger.LogError(ex, "Error with exception: {Message}", ex.Message);
    }

    [Benchmark]
    public void LogDirectly()
    {
        _uiLogService.Log("Direct log entry", SmartVoiceAgent.Core.Interfaces.LogLevel.Information, "Benchmark");
    }

    [Benchmark]
    public void LogAllLevels()
    {
        _logger.LogTrace("Trace message");
        _logger.LogDebug("Debug message");
        _logger.LogInformation("Info message");
        _logger.LogWarning("Warning message");
        _logger.LogError("Error message");
        _logger.LogCritical("Critical message");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _loggerProvider?.Dispose();
    }
}
