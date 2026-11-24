using System.Globalization;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Skills.Evaluation.Routing;

/// <summary>
/// Turns routing attempts into a Markdown summary.
/// </summary>
public static class SkillRoutingReport
{
    /// <summary>
    /// Builds the summary: one row per model and mode, then every miss.
    /// </summary>
    /// <param name="attempts">All attempts of the run.</param>
    /// <param name="header">Lines written above the table, such as date and case count.</param>
    public static string ToMarkdown(IReadOnlyCollection<SkillRoutingAttempt> attempts, IEnumerable<string> header)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Skill routing results");
        builder.AppendLine();
        foreach (var line in header)
        {
            builder.AppendLine($"- {line}");
        }

        builder.AppendLine();
        builder.AppendLine("| Model | Mode | Cases | Right skill | Right skill + args | First call right | EN skill | TR skill | Avg model calls | Median ms | Avg input tok | Avg output tok | Provider errors |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var group in Group(attempts))
        {
            var scored = group.Where(attempt => !attempt.TransportError).ToList();
            builder.AppendLine(string.Join(" | ", new[]
            {
                $"| `{group.Key.Model}`",
                ModeName(group.Key.Mode),
                scored.Count.ToString(CultureInfo.InvariantCulture),
                Rate(scored, attempt => attempt.SkillCorrect),
                Rate(scored, attempt => attempt.ArgumentsCorrect),
                Rate(scored, attempt => attempt.FirstCallCorrect),
                Rate(scored.Where(attempt => attempt.Language == "en").ToList(), attempt => attempt.SkillCorrect),
                Rate(scored.Where(attempt => attempt.Language == "tr").ToList(), attempt => attempt.SkillCorrect),
                Average(scored.Select(attempt => (long?)attempt.Steps), "0.0"),
                Median(scored.Select(attempt => attempt.DurationMilliseconds)),
                Average(scored.Select(attempt => attempt.InputTokens)),
                Average(scored.Select(attempt => attempt.OutputTokens)),
                (group.Count() - scored.Count).ToString(CultureInfo.InvariantCulture) + " |"
            }));
        }

        builder.AppendLine();
        builder.AppendLine("## Misses");
        builder.AppendLine();
        builder.AppendLine("Every attempt that did not pick the right skill with the expected arguments.");

        foreach (var group in Group(attempts))
        {
            var misses = group.Where(attempt => !attempt.ArgumentsCorrect).ToList();
            if (misses.Count == 0)
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine($"### `{group.Key.Model}`, {ModeName(group.Key.Mode)}");
            builder.AppendLine();
            builder.AppendLine("| Case | Picked | Arguments | Note |");
            builder.AppendLine("| --- | --- | --- | --- |");
            foreach (var miss in misses)
            {
                var note = miss.TransportError ? "provider error: " + miss.Error : miss.Error;
                builder.AppendLine(
                    $"| {miss.CaseId}.{miss.Language} | {Cell(miss.ChosenSkill)} | {Cell(miss.Arguments)} | {Cell(note)} |");
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<IGrouping<(string Model, SkillRoutingMode Mode), SkillRoutingAttempt>> Group(
        IEnumerable<SkillRoutingAttempt> attempts)
    {
        return attempts
            .GroupBy(attempt => (attempt.Model, attempt.Mode))
            .OrderBy(group => group.Key.Model, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Mode);
    }

    private static string ModeName(SkillRoutingMode mode)
    {
        return mode == SkillRoutingMode.JsonPlan ? "JSON plan" : "tool call";
    }

    private static string Rate(IReadOnlyCollection<SkillRoutingAttempt> attempts, Func<SkillRoutingAttempt, bool> passed)
    {
        if (attempts.Count == 0)
        {
            return "-";
        }

        var percent = 100.0 * attempts.Count(passed) / attempts.Count;
        return percent.ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private static string Median(IEnumerable<long> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return "-";
        }

        var middle = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        return median.ToString(CultureInfo.InvariantCulture);
    }

    private static string Average(IEnumerable<long?> values, string format = "0")
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return known.Length == 0
            ? "-"
            : known.Average().ToString(format, CultureInfo.InvariantCulture);
    }

    private static string Cell(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var text = value.Replace("|", "\\|", StringComparison.Ordinal);
        return text.Length <= 160 ? text : text[..160] + "...";
    }
}
