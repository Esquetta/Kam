using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Application.DependencyInjection;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Conf;
using SmartVoiceAgent.Infrastructure.Agent.Evaluation;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;
using SmartVoiceAgent.Infrastructure.DependencyInjection;
using SmartVoiceAgent.Infrastructure.Extensions;
using SmartVoiceAgent.Infrastructure.Skills.Audit;
using SmartVoiceAgent.Infrastructure.Skills.Execution;
using SmartVoiceAgent.Infrastructure.Skills.Planning;
using SmartVoiceAgent.Infrastructure.Skills.Policy;
using SmartVoiceAgent.Mailing.Extensions;

namespace SmartVoiceAgent.AgentHost.ConsoleApp;

public sealed class AgentTaskEvalOptions
{
    public string TasksPath { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public IReadOnlyList<string> Models { get; init; } = [];

    public string Provider { get; init; } = "OpenRouter";

    public string Endpoint { get; init; } = "https://openrouter.ai/api/v1";

    public string ApiKeyVariable { get; init; } = "OPENROUTER_API_KEY";
}

/// <summary>
/// <c>--agent-task-eval</c>: runs multi-step file tasks through the real agent loop, each in a fresh
/// temporary workspace, and checks the files afterwards. Only file tools inside the workspace may run.
/// </summary>
public static class AgentTaskEvalCommand
{
    public const string SwitchName = "--agent-task-eval";

    private static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(4);

    public static bool IsRequested(IEnumerable<string> args)
    {
        return args.Any(arg => arg.Equals(SwitchName, StringComparison.OrdinalIgnoreCase));
    }

    public static AgentTaskEvalOptions ParseOptions(IReadOnlyList<string> args)
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

        return new AgentTaskEvalOptions
        {
            TasksPath = Value("--tasks", Path.Combine("evals", "agent-tasks", "tasks.json")),
            OutputDirectory = Value("--out", Path.Combine("evals", "agent-tasks", "results", DateTime.Now.ToString("yyyy-MM-dd"))),
            Models = Value("--models", string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Provider = Value("--provider", "OpenRouter"),
            Endpoint = Value("--endpoint", "https://openrouter.ai/api/v1"),
            ApiKeyVariable = Value("--api-key-env", "OPENROUTER_API_KEY")
        };
    }

    public static async Task<int> RunAsync(
        AgentTaskEvalOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (options.Models.Count == 0)
        {
            await error.WriteLineAsync("Pass the models to compare with --models a,b,c.");
            return 2;
        }

        if (!File.Exists(options.TasksPath))
        {
            await error.WriteLineAsync($"Tasks file not found: {options.TasksPath}");
            return 2;
        }

        var apiKey = Environment.GetEnvironmentVariable(options.ApiKeyVariable) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey) && !options.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync($"Set {options.ApiKeyVariable} to the provider's API key.");
            return 2;
        }

        var tasks = AgentTaskCase.ParseAll(await File.ReadAllTextAsync(options.TasksPath, cancellationToken));
        var scratch = Path.Combine(Path.GetTempPath(), "kam-agent-task-eval", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(scratch);

        try
        {
            await using var services = BuildServices(scratch);
            var runtimeOptions = Options.Create(new AgentRuntimeOptions());
            var outputLock = new object();

            await output.WriteLineAsync($"{tasks.Count} tasks x {options.Models.Count} models.");

            var perModel = await Task.WhenAll(options.Models.Select(async model =>
            {
                var client = SmartVoiceAgent.Infrastructure.Extensions.ServiceCollectionExtensions.CreateChatClient(new AIServiceConfiguration
                {
                    Provider = options.Provider,
                    Endpoint = options.Endpoint,
                    ApiKey = apiKey,
                    ModelId = model
                }, sdkRetries: false);
                var sandbox = new AgentTaskSandbox(Path.Combine(scratch, "workspaces"));
                var runtime = new AgentRuntime(
                    _ => client,
                    services.GetRequiredService<IServiceScopeFactory>(),
                    sandbox,
                    new JsonAgentSessionStore(Path.Combine(scratch, "sessions", Guid.NewGuid().ToString("N")[..8])),
                    runtimeOptions,
                    NullLogger<AgentRuntime>.Instance,
                    workspaceRoot: () => sandbox.Root);

                var attempts = new List<AgentTaskAttempt>();
                foreach (var task in tasks)
                {
                    AgentTaskAttempt attempt;
                    try
                    {
                        attempt = await AgentTaskEvaluator.RunAsync(runtime, sandbox, model, task, TurnTimeout, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Record the failure and go on; one broken task must not lose the rest of the run.
                        attempt = new AgentTaskAttempt
                        {
                            CaseId = task.Id,
                            Language = task.Language,
                            Model = model,
                            Error = $"Eval error: {ex.GetType().Name}: {ex.Message}"
                        };
                    }

                    attempts.Add(attempt);
                    lock (outputLock)
                    {
                        output.WriteLine(
                            $"[{(attempt.Passed ? "PASS" : "FAIL")}] {model} {task.Id}.{task.Language} ({attempt.ToolCalls} calls, {attempt.DurationMilliseconds / 1000.0:0.0} s)");
                    }
                }

                return attempts;
            }));

            var all = perModel.SelectMany(attempts => attempts).ToList();
            Directory.CreateDirectory(options.OutputDirectory);
            await File.WriteAllLinesAsync(
                Path.Combine(options.OutputDirectory, "attempts.jsonl"),
                all.Select(attempt => JsonSerializer.Serialize(attempt, AgentTaskJson.Options)),
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(options.OutputDirectory, "summary.md"),
                AgentTaskReport.ToMarkdown(all,
                [
                    $"date: {DateTime.Now:yyyy-MM-dd}",
                    $"provider: {options.Provider} ({options.Endpoint})",
                    $"tasks: {tasks.Count} ({tasks.Count(task => task.Language == "en")} English, {tasks.Count(task => task.Language == "tr")} Turkish), from `{options.TasksPath.Replace('\\', '/')}`",
                    "the real agent loop and system prompt; every built-in skill is offered, but only file tools with paths inside the task's workspace may run",
                    $"one attempt per task; a turn may take {TurnTimeout.TotalMinutes:0} minutes"
                ]),
                cancellationToken);

            await output.WriteLineAsync($"Wrote {Path.Combine(options.OutputDirectory, "summary.md")}");
            return 0;
        }
        finally
        {
            AgentTaskSandbox.TryDelete(scratch);
        }
    }

    /// <summary>
    /// The app's own services, with the tool set reduced to built-in skills and every store that
    /// would write to the user's Kam folder replaced, so a run leaves no trace there.
    /// </summary>
    private static ServiceProvider BuildServices(string scratch)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApplicationServices();
        services.AddSmartVoiceAgent(configuration);
        services.AddInfrastructureServices(configuration);
        services.AddMailingServices(configuration);

        // Built-in skills only: no MCP servers, plugins or Agent Skills from the user's setup.
        services.RemoveAll<IAgentToolProvider>();
        services.AddScoped<IAgentToolProvider, SkillToolProvider>();

        services.Replace(ServiceDescriptor.Singleton<ISkillPolicyStore>(new JsonSkillPolicyStore(Path.Combine(scratch, "skill-policies.json"))));
        services.Replace(ServiceDescriptor.Singleton<ISkillAuditLogService>(new JsonSkillAuditLogService(Path.Combine(scratch, "skill-audit.jsonl"))));
        services.Replace(ServiceDescriptor.Singleton<ISkillExecutionHistoryService>(new InMemorySkillExecutionHistoryService()));
        services.Replace(ServiceDescriptor.Singleton<ISkillPlannerTraceStore>(new InMemorySkillPlannerTraceStore()));

        return services.BuildServiceProvider();
    }
}
