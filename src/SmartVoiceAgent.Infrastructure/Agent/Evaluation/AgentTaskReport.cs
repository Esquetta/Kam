using System.Globalization;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Agent.Evaluation;

/// <summary>
/// Turns task attempts into a Markdown summary.
/// </summary>
public static class AgentTaskReport
{
    /// <summary>
    /// Builds the summary: one row per model, then every failed task.
    /// </summary>
    /// <param name="attempts">All attempts of the run.</param>
    /// <param name="header">Lines written above the table.</param>
    public static string ToMarkdown(IReadOnlyCollection<AgentTaskAttempt> attempts, IEnumerable<string> header)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Agent task results");
        builder.AppendLine();
        foreach (var line in header)
        {
            builder.AppendLine($"- {line}");
        }

        builder.AppendLine();
        builder.AppendLine("| Model | Tasks | Passed | EN | TR | Reply in request language | Avg tool calls | Failed calls | Blocked calls | Median s | Avg input tok | Avg output tok | Turn errors |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        var groups = attempts.GroupBy(attempt => attempt.Model).OrderBy(group => group.Key, StringComparer.Ordinal).ToList();
        foreach (var group in groups)
        {
            var all = group.ToList();
            builder.AppendLine(string.Join(" | ", new[]
            {
                $"| `{group.Key}`",
                all.Count.ToString(CultureInfo.InvariantCulture),
                Rate(all),
                Rate(all.Where(attempt => attempt.Language == "en").ToList()),
                Rate(all.Where(attempt => attempt.Language == "tr").ToList()),
                Percent(all, attempt => attempt.ReplyInRequestLanguage),
                all.Average(attempt => attempt.ToolCalls).ToString("0.0", CultureInfo.InvariantCulture),
                all.Sum(attempt => attempt.FailedToolCalls).ToString(CultureInfo.InvariantCulture),
                all.Sum(attempt => attempt.BlockedToolCalls).ToString(CultureInfo.InvariantCulture),
                MedianSeconds(all),
                Math.Round(all.Average(attempt => attempt.InputTokens)).ToString(CultureInfo.InvariantCulture),
                Math.Round(all.Average(attempt => attempt.OutputTokens)).ToString(CultureInfo.InvariantCulture),
                all.Count(attempt => !string.IsNullOrEmpty(attempt.Error)).ToString(CultureInfo.InvariantCulture) + " |"
            }));
        }

        builder.AppendLine();
        builder.AppendLine("## Failed tasks");

        foreach (var group in groups)
        {
            var failed = group.Where(attempt => !attempt.Passed).ToList();
            if (failed.Count == 0)
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine($"### `{group.Key}`");
            builder.AppendLine();
            builder.AppendLine("| Task | Failed checks | Tools | Blocked calls | Reply or error |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var attempt in failed)
            {
                var note = string.IsNullOrEmpty(attempt.Error) ? attempt.FinalText : "error: " + attempt.Error;
                builder.AppendLine(
                    $"| {attempt.CaseId}.{attempt.Language} | {Cell(string.Join("; ", attempt.FailedChecks))} | {Cell(string.Join(", ", attempt.Tools))} | {Cell(string.Join("; ", attempt.Blocked))} | {Cell(note)} |");
            }
        }

        return builder.ToString();
    }

    private static string Rate(IReadOnlyCollection<AgentTaskAttempt> attempts)
    {
        return Percent(attempts, attempt => attempt.Passed);
    }

    private static string Percent(IReadOnlyCollection<AgentTaskAttempt> attempts, Func<AgentTaskAttempt, bool> predicate)
    {
        return attempts.Count == 0
            ? "-"
            : (100.0 * attempts.Count(predicate) / attempts.Count).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private static string MedianSeconds(IReadOnlyCollection<AgentTaskAttempt> attempts)
    {
        var sorted = attempts.Select(attempt => attempt.DurationMilliseconds).Order().ToArray();
        var middle = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
        return (median / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string Cell(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var text = value.Replace("|", "\\|", StringComparison.Ordinal);
        return text.Length <= 200 ? text : text[..200] + "...";
    }
}
