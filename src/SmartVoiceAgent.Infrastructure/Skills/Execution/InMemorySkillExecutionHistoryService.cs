using System.Text.Json;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Execution;

public sealed class InMemorySkillExecutionHistoryService : ISkillExecutionHistoryService
{
    private const int DefaultMaxEntries = 100;
    private const int MaxArgumentValueLength = 120;
    private const int MaxArgumentsSummaryLength = 600;
    private const int MaxResultSummaryLength = 2000;
    private const int MaxOutputLength = 8000;

    private static readonly string[] SensitiveArgumentNames =
    [
        "apiKey",
        "authorization",
        "auth",
        "credential",
        "key",
        "password",
        "secret",
        "token"
    ];

    private readonly object _gate = new();
    private readonly List<SkillExecutionHistoryEntry> _entries = [];
    private readonly int _maxEntries;

    public InMemorySkillExecutionHistoryService(int maxEntries = DefaultMaxEntries)
    {
        _maxEntries = Math.Max(1, maxEntries);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<SkillExecutionHistoryEntry> GetRecent(int maxCount = 50)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            return _entries.Take(maxCount).ToArray();
        }
    }

    public SkillExecutionHistoryEntry Record(
        SkillPlan plan,
        SkillResult result,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);

        var shellResult = result.Data as ShellCommandResult;
        var entry = new SkillExecutionHistoryEntry
        {
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            SkillId = plan.SkillId,
            ArgumentsSummary = SummarizeArguments(plan),
            Success = result.Success,
            Status = result.Status,
            ErrorCode = result.ErrorCode,
            ResultSummary = Limit(GetResultSummary(result), MaxResultSummaryLength),
            DurationMilliseconds = result.DurationMilliseconds > 0
                ? result.DurationMilliseconds
                : shellResult?.DurationMilliseconds ?? 0,
            Command = shellResult?.Command ?? string.Empty,
            WorkingDirectory = shellResult?.WorkingDirectory ?? string.Empty,
            ExitCode = shellResult?.ExitCode,
            StdOut = Limit(shellResult?.StdOut ?? string.Empty, MaxOutputLength),
            StdErr = Limit(shellResult?.StdErr ?? string.Empty, MaxOutputLength),
            TimedOut = shellResult?.TimedOut ?? result.Status == SkillExecutionStatus.TimedOut,
            Cancelled = shellResult?.Cancelled ?? result.Status == SkillExecutionStatus.Cancelled,
            Truncated = shellResult?.Truncated ?? false
        };

        lock (_gate)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > _maxEntries)
            {
                _entries.RemoveRange(_maxEntries, _entries.Count - _maxEntries);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string SummarizeArguments(SkillPlan plan)
    {
        if (plan.Arguments.Count == 0)
        {
            return string.Empty;
        }

        var parts = plan.Arguments
            .Take(12)
            .Select(argument => $"{argument.Key}={SummarizeArgument(argument.Key, argument.Value)}");

        return Limit(string.Join(", ", parts), MaxArgumentsSummaryLength);
    }

    private static string SummarizeArgument(string name, JsonElement value)
    {
        if (IsSensitive(name))
        {
            return "<redacted>";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => Limit(value.GetString() ?? string.Empty, MaxArgumentValueLength),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => $"array[{value.GetArrayLength()}]",
            JsonValueKind.Object => "object",
            _ => value.GetRawText()
        };
    }

    private static bool IsSensitive(string name)
    {
        return SensitiveArgumentNames.Any(sensitive =>
            name.Contains(sensitive, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetResultSummary(SkillResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            return result.Message;
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        return result.Status.ToString();
    }

    private static string Limit(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }
}
