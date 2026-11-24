using System.Text.Json;
using SmartVoiceAgent.Infrastructure.Agent.Conf;
using SmartVoiceAgent.Infrastructure.Extensions;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn;
using SmartVoiceAgent.Infrastructure.Skills.Evaluation.Routing;

namespace SmartVoiceAgent.AgentHost.ConsoleApp;

public sealed class SkillRoutingEvalOptions
{
    public string CasesPath { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public IReadOnlyList<string> Models { get; init; } = [];

    public IReadOnlyList<SkillRoutingMode> Modes { get; init; } = [SkillRoutingMode.JsonPlan, SkillRoutingMode.ToolCall];

    public string Provider { get; init; } = "OpenRouter";

    public string Endpoint { get; init; } = "https://openrouter.ai/api/v1";

    public string ApiKeyVariable { get; init; } = "OPENROUTER_API_KEY";
}

/// <summary>
/// <c>--skill-routing-eval</c>: asks each model to route every case to a skill, as a JSON plan
/// and as a native tool call, and writes the raw attempts and a Markdown summary.
/// </summary>
public static class SkillRoutingEvalCommand
{
    public const string SwitchName = "--skill-routing-eval";

    // The agent loop does not offer the old single-shot agent as a tool, so neither mode sees it.
    private static readonly HashSet<string> ExcludedSkills = new(StringComparer.OrdinalIgnoreCase) { "agents.run" };

    public static bool IsRequested(IEnumerable<string> args)
    {
        return args.Any(arg => arg.Equals(SwitchName, StringComparison.OrdinalIgnoreCase));
    }

    public static SkillRoutingEvalOptions ParseOptions(IReadOnlyList<string> args)
    {
        string Value(string name, string fallback)
        {
            for (var index = 0; index < args.Count - 1; index++)
            {
                if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return fallback;
        }

        static IReadOnlyList<string> List(string value)
        {
            return value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        var modes = List(Value("--modes", "json,tool"))
            .Select(mode => mode.ToLowerInvariant() switch
            {
                "json" => SkillRoutingMode.JsonPlan,
                "tool" => SkillRoutingMode.ToolCall,
                _ => throw new ArgumentException($"Unknown mode '{mode}'. Use json, tool or both.")
            })
            .Distinct()
            .ToArray();

        return new SkillRoutingEvalOptions
        {
            CasesPath = Value("--cases", Path.Combine("evals", "skill-routing", "cases.json")),
            OutputDirectory = Value("--out", Path.Combine("evals", "skill-routing", "results", DateTime.Now.ToString("yyyy-MM-dd"))),
            Models = List(Value("--models", string.Empty)),
            Modes = modes,
            Provider = Value("--provider", "OpenRouter"),
            Endpoint = Value("--endpoint", "https://openrouter.ai/api/v1"),
            ApiKeyVariable = Value("--api-key-env", "OPENROUTER_API_KEY")
        };
    }

    public static async Task<int> RunAsync(
        SkillRoutingEvalOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (options.Models.Count == 0)
        {
            await error.WriteLineAsync("Pass the models to compare with --models a,b,c.");
            return 2;
        }

        if (!File.Exists(options.CasesPath))
        {
            await error.WriteLineAsync($"Cases file not found: {options.CasesPath}");
            return 2;
        }

        var apiKey = Environment.GetEnvironmentVariable(options.ApiKeyVariable) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey) && !options.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync($"Set {options.ApiKeyVariable} to the provider's API key.");
            return 2;
        }

        var cases = SkillRoutingCase.ParseAll(await File.ReadAllTextAsync(options.CasesPath, cancellationToken));
        var skills = BuiltInSkillManifestCatalog.CreateAll()
            .Where(skill => skill.Enabled && !skill.ReviewRequired && !ExcludedSkills.Contains(skill.Id))
            .ToArray();
        var evaluator = new SkillRoutingEvaluator(skills, new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero));

        await output.WriteLineAsync(
            $"{cases.Count} cases x {options.Modes.Count} modes x {options.Models.Count} models, {evaluator.SkillCount} skills offered.");

        var outputLock = new object();
        var perModel = await Task.WhenAll(options.Models.Select(async model =>
        {
            var client = ServiceCollectionExtensions.CreateChatClient(new AIServiceConfiguration
            {
                Provider = options.Provider,
                Endpoint = options.Endpoint,
                ApiKey = apiKey,
                ModelId = model
            });

            var attempts = new List<SkillRoutingAttempt>();
            foreach (var testCase in cases)
            {
                foreach (var mode in options.Modes)
                {
                    var attempt = await evaluator.RunAsync(client, model, testCase, mode, cancellationToken);
                    attempts.Add(attempt);
                    lock (outputLock)
                    {
                        var mark = attempt.TransportError ? "ERR " : attempt.ArgumentsCorrect ? "PASS" : attempt.SkillCorrect ? "ARGS" : "MISS";
                        output.WriteLine($"[{mark}] {model} {mode} {testCase.Id}.{testCase.Language} -> {attempt.ChosenSkill} ({attempt.DurationMilliseconds} ms)");
                    }
                }
            }

            return attempts;
        }));

        var all = perModel.SelectMany(attempts => attempts).ToList();
        Directory.CreateDirectory(options.OutputDirectory);
        await File.WriteAllLinesAsync(
            Path.Combine(options.OutputDirectory, "attempts.jsonl"),
            all.Select(attempt => JsonSerializer.Serialize(attempt, SkillRoutingJson.Options)),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(options.OutputDirectory, "summary.md"),
            SkillRoutingReport.ToMarkdown(all,
            [
                $"date: {DateTime.Now:yyyy-MM-dd}",
                $"provider: {options.Provider} ({options.Endpoint})",
                $"cases: {cases.Count} ({cases.Count(testCase => testCase.Language == "en")} English, {cases.Count(testCase => testCase.Language == "tr")} Turkish), from `{options.CasesPath.Replace('\\', '/')}`",
                $"skills offered: {evaluator.SkillCount} built-in skills, the same set in both modes",
                "one attempt per case; provider errors are retried twice and then left out of the rates"
            ]),
            cancellationToken);

        await output.WriteLineAsync($"Wrote {Path.Combine(options.OutputDirectory, "summary.md")}");
        return all.All(attempt => attempt.TransportError) ? 1 : 0;
    }
}
