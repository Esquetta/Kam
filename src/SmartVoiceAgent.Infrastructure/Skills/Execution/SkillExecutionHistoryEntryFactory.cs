using System.Text.Json;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Execution;

internal static class SkillExecutionHistoryEntryFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    public static SkillExecutionHistoryEntry Create(
        SkillPlan plan,
        SkillResult result,
        DateTimeOffset? timestamp = null)
    {
        var shellResult = result.Data as ShellCommandResult;
        var canReplay = CanReplay(plan, out var replayBlockedReason);
        return new SkillExecutionHistoryEntry
        {
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            SkillId = plan.SkillId,
            ArgumentsSummary = SummarizeArguments(plan),
            ReplayPlanJson = JsonSerializer.Serialize(plan, JsonOptions),
            CanReplay = canReplay,
            ReplayBlockedReason = replayBlockedReason,
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
    }

    private static bool CanReplay(SkillPlan plan, out string blockedReason)
    {
        if (plan.RequiresConfirmation)
        {
            blockedReason = "Replay requires confirmation.";
            return false;
        }

        if (IsHighRiskReplayBlocked(plan.SkillId))
        {
            blockedReason = "Replay is blocked for high-risk write actions.";
            return false;
        }

        blockedReason = string.Empty;
        return true;
    }

    private static bool IsHighRiskReplayBlocked(string skillId)
    {
        return skillId.Equals("shell.run", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("file.patch", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("file.replace_range", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("files.create", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("files.copy", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("files.move", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("file.write", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("files.write", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("file.delete", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("files.delete", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("clipboard.set", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("clipboard.clear", StringComparison.OrdinalIgnoreCase);
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
