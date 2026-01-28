using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services.UiLog;

/// <summary>
/// Logger provider that forwards logs to the UI log service
/// </summary>
public class UiLogLoggerProvider : ILoggerProvider
{
    private readonly IUiLogService _uiLogService;

    public UiLogLoggerProvider(IUiLogService uiLogService)
    {
        _uiLogService = uiLogService;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new UiLogLogger(_uiLogService, categoryName);
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}

/// <summary>
/// Logger implementation that sends logs to UI
/// </summary>
public class UiLogLogger : ILogger
{
    private readonly IUiLogService _uiLogService;
    private readonly string _categoryName;

    public UiLogLogger(IUiLogService uiLogService, string categoryName)
    {
        _uiLogService = uiLogService;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        return logLevel >= Microsoft.Extensions.Logging.LogLevel.Information;
    }

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message))
            return;

        // Extract agent name from category if present
        var source = _categoryName.Split('.').LastOrDefault() ?? _categoryName;
        
        // Map Microsoft log level to our log level
        var level = logLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Debug => Core.Interfaces.LogLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Information => Core.Interfaces.LogLevel.Information,
            Microsoft.Extensions.Logging.LogLevel.Warning => Core.Interfaces.LogLevel.Warning,
            Microsoft.Extensions.Logging.LogLevel.Error => Core.Interfaces.LogLevel.Error,
            Microsoft.Extensions.Logging.LogLevel.Critical => Core.Interfaces.LogLevel.Critical,
            _ => Core.Interfaces.LogLevel.Information
        };

        _uiLogService.Log(message, level, source);

        // Also log exception if present
        if (exception != null)
        {
            _uiLogService.Log($"Exception: {exception.Message}", Core.Interfaces.LogLevel.Error, source);
        }
    }
}
