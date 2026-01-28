using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Ui.ViewModels;
using System;

namespace SmartVoiceAgent.Ui.Services.Concrete;

/// <summary>
/// UI implementation of IUiLogService that forwards logs to the MainWindowViewModel
/// </summary>
public class UiLogService : IUiLogService
{
    private MainWindowViewModel? _mainViewModel;

    public event EventHandler<UiLogEntry>? OnLogEntry;

    public void SetViewModel(MainWindowViewModel viewModel)
    {
        _mainViewModel = viewModel;
    }

    public void Log(string message, LogLevel level = LogLevel.Information, string? source = null)
    {
        var entry = new UiLogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
            Level = level,
            Source = source,
            IsAgentUpdate = false
        };

        // Raise event for any subscribers
        OnLogEntry?.Invoke(this, entry);

        // Forward to ViewModel
        _mainViewModel?.AddLog(FormatLogEntry(entry));
    }

    public void LogAgentUpdate(string agentName, string message, bool isComplete = false)
    {
        var entry = new UiLogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
            Level = LogLevel.Information,
            AgentName = agentName,
            IsAgentUpdate = true,
            IsComplete = isComplete
        };

        OnLogEntry?.Invoke(this, entry);
        _mainViewModel?.AddLog(FormatLogEntry(entry));
    }

    private string FormatLogEntry(UiLogEntry entry)
    {
        if (entry.IsAgentUpdate && !string.IsNullOrEmpty(entry.AgentName))
        {
            var prefix = entry.IsComplete ? "✓" : "►";
            return $"{prefix} [{entry.AgentName.ToUpper()}] {entry.Message}";
        }

        if (!string.IsNullOrEmpty(entry.Source))
        {
            return $"[{entry.Source}] {entry.Message}";
        }

        return entry.Message;
    }
}
