using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.AgentHost.ConsoleApp;

public sealed class SkillSmokeOptions
{
    public string? SummaryPath { get; init; }

    public IReadOnlyCollection<string> SkillIds { get; init; } = [];

    public bool ListOnly { get; init; }
}

public sealed class SkillSmokeCommand
{
    public const string SwitchName = "--skill-smoke";

    private readonly ISkillEvalHarness _harness;
    private readonly ISkillEvalCaseCatalog _catalog;

    public SkillSmokeCommand(
        ISkillEvalHarness harness,
        ISkillEvalCaseCatalog catalog)
    {
        _harness = harness;
        _catalog = catalog;
    }

    public static bool IsRequested(IEnumerable<string> args)
    {
        return args.Any(arg => arg.Equals(SwitchName, StringComparison.OrdinalIgnoreCase));
    }

    public static SkillSmokeOptions ParseOptions(IReadOnlyList<string> args)
    {
        var skillIds = new List<string>();
        var summaryPath = string.Empty;
        var listOnly = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg.Equals(SwitchName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (arg.Equals("--list-skill-smoke-cases", StringComparison.OrdinalIgnoreCase))
            {
                listOnly = true;
                continue;
            }

            if (IsValueSwitch(arg, "--summary", "--skill-smoke-summary") && index + 1 < args.Count)
            {
                summaryPath = args[++index];
                continue;
            }

            if (IsValueSwitch(arg, "--skills", "--skill-smoke-skills") && index + 1 < args.Count)
            {
                skillIds.AddRange(SplitSkillIds(args[++index]));
            }
        }

        return new SkillSmokeOptions
        {
            SummaryPath = string.IsNullOrWhiteSpace(summaryPath) ? null : summaryPath,
            SkillIds = skillIds
                .Where(skillId => !string.IsNullOrWhiteSpace(skillId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ListOnly = listOnly
        };
    }

    public async Task<int> RunAsync(
        SkillSmokeOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var cases = FilterCases(_catalog.CreateSmokeCases(), options.SkillIds);
        if (cases.Count == 0)
        {
            await error.WriteLineAsync("No skill smoke cases matched the requested filter.");
            return 2;
        }

        if (options.ListOnly)
        {
            foreach (var testCase in cases)
            {
                await output.WriteLineAsync($"{testCase.Plan.SkillId} - {testCase.Name}");
            }

            return 0;
        }

        var summary = await _harness.RunAsync(cases, cancellationToken);
        foreach (var result in summary.Results)
        {
            var status = result.Passed ? "PASS" : "FAIL";
            await output.WriteLineAsync(
                $"[{status}] {result.SkillId} - {result.Name} ({result.DurationMilliseconds} ms)");
        }

        if (!string.IsNullOrWhiteSpace(options.SummaryPath))
        {
            var directory = Path.GetDirectoryName(options.SummaryPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                options.SummaryPath,
                FormatMarkdown(summary),
                cancellationToken);
        }

        if (summary.Failed > 0)
        {
            await error.WriteLineAsync($"Skill smoke failed: {summary.Failed}/{summary.Total} failed");
            return 1;
        }

        await output.WriteLineAsync($"Skill smoke completed: {summary.Passed}/{summary.Total} passed");
        return 0;
    }

    private static IReadOnlyList<SkillEvalCase> FilterCases(
        IReadOnlyCollection<SkillEvalCase> cases,
        IReadOnlyCollection<string> skillIds)
    {
        if (skillIds.Count == 0)
        {
            return cases.ToArray();
        }

        var filter = skillIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return cases
            .Where(testCase => filter.Contains(testCase.Plan.SkillId))
            .ToArray();
    }

    private static string FormatMarkdown(SkillEvalSummary summary)
    {
        var lines = new List<string>
        {
            "# Skill Smoke",
            string.Empty,
            $"- timestamp: {DateTimeOffset.Now:o}",
            $"- status: {(summary.Failed == 0 ? "completed" : "failed")}",
            $"- total: {summary.Total}",
            $"- passed: {summary.Passed}",
            $"- failed: {summary.Failed}",
            string.Empty,
            "## Results"
        };

        foreach (var result in summary.Results)
        {
            var status = result.Passed ? "PASS" : "FAIL";
            lines.Add($"- [{status}] `{result.SkillId}` - {result.Name} ({result.DurationMilliseconds} ms)");
            lines.Add($"  - expected: {result.ExpectedStatus}");
            lines.Add($"  - actual: {result.ActualStatus}");
            lines.Add($"  - message: {NormalizeMessage(result.Message)}");
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "(empty)";
        }

        var normalized = message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        const int maxLength = 500;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private static bool IsValueSwitch(string arg, params string[] names)
    {
        return names.Any(name => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> SplitSkillIds(string value)
    {
        return value.Split(
            [',', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
