using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Infrastructure.Agent.Tools;
using SmartVoiceAgent.Infrastructure.Mcp;
using System.Net.Sockets;
using System.Reflection;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Tools;

public class TaskAgentToolsTests
{
    [Fact]
    public async Task GetToolsAsync_WhenTodoistMcpIsNotConfigured_DisablesMcpWithoutError()
    {
        var logger = new RecordingLogger<TaskAgentTools>();
        var tools = new TaskAgentTools(
            Options.Create(new McpOptions
            {
                TodoistApiKey = string.Empty,
                TodoistServerLink = string.Empty
            }),
            logger);

        var asyncTools = await tools.GetToolsAsync();
        var syncTools = tools.GetTools();

        asyncTools.Should().BeEmpty();
        syncTools.Should().BeEmpty();
        logger.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Error);
        logger.Entries.Should().NotContain(entry =>
            entry.Message.Contains("GetTools() called before initialization", StringComparison.Ordinal));
    }

    [Fact]
    public void RetryPolicy_WhenDnsHostCannotBeResolved_DoesNotRetry()
    {
        var socketException = new SocketException((int)SocketError.HostNotFound);
        var exception = new HttpRequestException("No such host is known.", socketException);

        InvokeRetryableExceptionPolicy(exception).Should().BeFalse();
    }

    private static bool InvokeRetryableExceptionPolicy(Exception exception)
    {
        var method = typeof(TaskAgentTools).GetMethod(
            "IsRetryableException",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        return (bool)method!.Invoke(null, [exception])!;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
