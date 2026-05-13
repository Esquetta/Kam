using System.Text;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.CodingAgent;
using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Session;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Core.Models.SlashCommands;
using SmartVoiceAgent.Core.Security;
using SmartVoiceAgent.Infrastructure.Agent.Conf;
using SmartVoiceAgent.Infrastructure.Mcp;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class SlashCommandService : ISlashCommandService
{
    private static readonly SlashCommandDefinition[] Definitions =
    [
        new("/help", "Show available slash commands.", "/help", "General", ["/commands"]),
        new("/commands", "List slash commands, optionally filtered.", "/commands [filter]", "General", ["/help"]),
        new("/settings", "Open the settings screen.", "/settings", "Navigation"),
        new("/integrations", "Open integration settings.", "/integrations", "Navigation"),
        new("/diagnostics", "Open runtime diagnostics.", "/diagnostics", "Navigation", ["/runtime"]),
        new("/coordinator", "Open the coordinator workspace.", "/coordinator", "Navigation", ["/home"]),
        new("/theme", "Toggle the active theme.", "/theme", "General"),
        new("/voice", "Toggle voice capture.", "/voice", "Runtime"),
        new("/version", "Show current Kam version and update channel.", "/version", "Updates"),
        new("/update", "Check for a newer Kam release.", "/update [check|download|restart]", "Updates"),
        new("/download", "Download the latest Kam release package.", "/download", "Updates"),
        new("/restart", "Show the Kam restart handoff plan.", "/restart [packagePath]", "Updates"),
        new("/readiness", "Show first-run setup readiness and next actions.", "/readiness", "Runtime", ["/setup", "/first-run"]),
        new("/status", "Show runtime and skill health status.", "/status", "Runtime"),
        new("/model", "Show the active runtime AI provider and selected model.", "/model", "Runtime", ["/models"]),
        new("/limits", "Show rate-limit, quota, and balance warning behavior.", "/limits", "Runtime", ["/quota", "/balance", "/rate-limit"]),
        new("/permissions", "Show chat command permission boundaries.", "/permissions", "Runtime"),
        new("/diff", "Show how to inspect the workspace diff from coding-agent mode.", "/diff", "Workflow"),
        new("/dependabot", "Show how to run dependency audit and Dependabot checks.", "/dependabot", "Workflow"),
        new("/github", "Show how to inspect GitHub PR and workflow status.", "/github", "Workflow"),
        new("/github app", "Show GitHub App connection status.", "/github app", "Workflow", ["/github-app"]),
        new("/github app actions", "List GitHub Actions workflow runs visible through the configured GitHub App.", "/github app actions", "Workflow"),
        new("/github app context", "Show the active GitHub repository, PR, and workflow run context.", "/github app context", "Workflow"),
        new("/github app diagnose", "Diagnose a GitHub Actions workflow run from jobs and failed steps.", "/github app diagnose <owner/repo> [runId]", "Workflow"),
        new("/github app fix", "Open a runtime auto-fix loop for a failing workflow run.", "/github app fix <owner/repo> [runId]", "Workflow"),
        new("/github app logs", "Get a temporary download URL or preview for one GitHub Actions job log.", "/github app logs <owner/repo> <jobId>", "Workflow"),
        new("/github app prs", "List open pull requests visible through the configured GitHub App.", "/github app prs", "Workflow"),
        new("/github app repos", "List repositories visible through the configured GitHub App.", "/github app repos", "Workflow"),
        new("/github app run", "List jobs for one GitHub Actions workflow run.", "/github app run <owner/repo> <runId>", "Workflow"),
        new("/github actions", "List GitHub Actions workflow runs visible through the configured GitHub App.", "/github actions", "Workflow"),
        new("/github context", "Show the active GitHub repository, PR, and workflow run context.", "/github context", "Workflow"),
        new("/github diagnose", "Diagnose a GitHub Actions workflow run from jobs and failed steps.", "/github diagnose <owner/repo> [runId]", "Workflow", ["/github doctor", "/github ci"]),
        new("/github fix", "Open a runtime auto-fix loop for a failing workflow run.", "/github fix <owner/repo> [runId]", "Workflow", ["/github autofix", "/github auto-fix"]),
        new("/github logs", "Get a temporary download URL or preview for one GitHub Actions job log.", "/github logs <owner/repo> <jobId>", "Workflow", ["/github log"]),
        new("/github prs", "List open pull requests visible through the configured GitHub App.", "/github prs", "Workflow", ["/github pr", "/github pull-requests"]),
        new("/github repos", "List repositories visible through the configured GitHub App.", "/github repos", "Workflow"),
        new("/github run", "List jobs for one GitHub Actions workflow run.", "/github run <owner/repo> <runId>", "Workflow", ["/github jobs"]),
        new("/github-app", "Show GitHub App setup and repository permission guidance.", "/github-app", "Workflow"),
        new("/github-app actions", "List GitHub Actions workflow runs visible through the configured GitHub App.", "/github-app actions", "Workflow"),
        new("/github-app context", "Show the active GitHub repository, PR, and workflow run context.", "/github-app context", "Workflow"),
        new("/github-app diagnose", "Diagnose a GitHub Actions workflow run from jobs and failed steps.", "/github-app diagnose <owner/repo> [runId]", "Workflow", ["/github-app doctor", "/github-app ci"]),
        new("/github-app fix", "Open a runtime auto-fix loop for a failing workflow run.", "/github-app fix <owner/repo> [runId]", "Workflow", ["/github-app autofix", "/github-app auto-fix"]),
        new("/github-app logs", "Get a temporary download URL or preview for one GitHub Actions job log.", "/github-app logs <owner/repo> <jobId>", "Workflow", ["/github-app log"]),
        new("/github-app prs", "List open pull requests visible through the configured GitHub App.", "/github-app prs", "Workflow", ["/github-app pr", "/github-app pull-requests"]),
        new("/github-app repos", "List repositories visible through the configured GitHub App.", "/github-app repos", "Workflow"),
        new("/github-app run", "List jobs for one GitHub Actions workflow run.", "/github-app run <owner/repo> <runId>", "Workflow", ["/github-app jobs"]),
        new("/plugins", "Show skill/plugin health summary.", "/plugins", "Skills"),
        new("/mcp", "Show configured MCP endpoint status.", "/mcp", "Integrations"),
        new("/agent", "Create a short-lived task agent for one request.", "/agent <task>", "Runtime", ["/task-agent"]),
        new("/agents", "Show registered runtime agents.", "/agents", "Runtime"),
        new("/agents cancel", "Cancel a running runtime agent task.", "/agents cancel <runId>", "Runtime"),
        new("/agents retry", "Queue a retry from a previous runtime agent task.", "/agents retry <runId>", "Runtime"),
        new("/test", "Run a registered smoke test for one skill.", "/test <skillId>", "Skills"),
        new("/review", "Review current skill health state.", "/review", "Skills"),
        new("/worktree", "Show worktree command availability.", "/worktree", "Workflow"),
        new("/hooks", "Show coding-agent hook availability.", "/hooks", "Workflow"),
        new("/clear", "Clear the command input.", "/clear", "General")
    ];

    private readonly IVoiceAgentHostControl? _hostControl;
    private readonly ISkillHealthService? _skillHealthService;
    private readonly ISkillTestService? _skillTestService;
    private readonly ISkillEvalCaseCatalog? _evalCaseCatalog;
    private readonly IAgentRegistry? _agentRegistry;
    private readonly CodingAgentOptions _codingAgentOptions;
    private readonly McpOptions _mcpOptions;
    private readonly IApplicationUpdateService? _applicationUpdateService;
    private readonly IApplicationVersionProvider? _applicationVersionProvider;
    private readonly IApplicationUpdateSession _applicationUpdateSession;
    private readonly IApplicationRestartPlanner? _applicationRestartPlanner;
    private readonly IGitHubAppClient? _githubAppClient;
    private readonly AIServiceConfiguration _aiServiceConfiguration;
    private readonly ISkillExecutionPipeline? _skillExecutionPipeline;
    private readonly IRuntimeAgentRunStore? _runtimeAgentRunStore;
    private readonly IApplicationSessionContextStore? _applicationSessionContextStore;
    private string? _activeGitHubRepositoryFullName;
    private GitHubWorkflowRunSummary? _activeGitHubWorkflowRun;
    private GitHubPullRequestSummary? _activeGitHubPullRequest;

    public SlashCommandService(
        IVoiceAgentHostControl? hostControl = null,
        ISkillHealthService? skillHealthService = null,
        ISkillTestService? skillTestService = null,
        ISkillEvalCaseCatalog? evalCaseCatalog = null,
        IAgentRegistry? agentRegistry = null,
        IOptions<CodingAgentOptions>? codingAgentOptions = null,
        IOptions<McpOptions>? mcpOptions = null,
        IApplicationUpdateService? applicationUpdateService = null,
        IApplicationVersionProvider? applicationVersionProvider = null,
        IApplicationUpdateSession? applicationUpdateSession = null,
        IApplicationRestartPlanner? applicationRestartPlanner = null,
        IGitHubAppClient? githubAppClient = null,
        IOptions<AIServiceConfiguration>? aiServiceOptions = null,
        ISkillExecutionPipeline? skillExecutionPipeline = null,
        IRuntimeAgentRunStore? runtimeAgentRunStore = null,
        IApplicationSessionContextStore? applicationSessionContextStore = null)
    {
        _hostControl = hostControl;
        _skillHealthService = skillHealthService;
        _skillTestService = skillTestService;
        _evalCaseCatalog = evalCaseCatalog;
        _agentRegistry = agentRegistry;
        _codingAgentOptions = codingAgentOptions?.Value ?? new CodingAgentOptions();
        _mcpOptions = mcpOptions?.Value ?? new McpOptions();
        _applicationUpdateService = applicationUpdateService;
        _applicationVersionProvider = applicationVersionProvider;
        _applicationUpdateSession = applicationUpdateSession ?? new ApplicationUpdateSession();
        _applicationRestartPlanner = applicationRestartPlanner;
        _githubAppClient = githubAppClient;
        _aiServiceConfiguration = aiServiceOptions?.Value ?? new AIServiceConfiguration();
        _skillExecutionPipeline = skillExecutionPipeline;
        _runtimeAgentRunStore = runtimeAgentRunStore;
        _applicationSessionContextStore = applicationSessionContextStore;
        RestoreApplicationSessionContext();
    }

    public IReadOnlyList<SlashCommandDefinition> GetCommands()
    {
        return Definitions;
    }

    public IReadOnlyList<SlashCommandDefinition> GetSuggestions(string input)
    {
        if (!IsSlashCommand(input))
        {
            return [];
        }

        var filter = GetCommandToken(input).TrimStart('/');
        return Definitions
            .Where(command => string.IsNullOrWhiteSpace(filter)
                || command.Name.TrimStart('/').Contains(filter, StringComparison.OrdinalIgnoreCase)
                || command.Aliases.Any(alias => alias.TrimStart('/').Contains(filter, StringComparison.OrdinalIgnoreCase))
                || command.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || command.Category.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool IsSlashCommand(string input)
    {
        return !string.IsNullOrWhiteSpace(input)
            && input.TrimStart().StartsWith("/", StringComparison.Ordinal);
    }

    public async Task<SlashCommandResult> ExecuteAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (!IsSlashCommand(input))
        {
            return SlashCommandResult.Failed(string.Empty, "Input is not a slash command.");
        }

        var commandName = GetCommandToken(input).ToLowerInvariant();
        var arguments = GetArguments(input);
        var argumentText = GetArgumentText(input);
        var normalizedName = NormalizeAlias(commandName);

        return normalizedName switch
        {
            "/help" or "/commands" => SlashCommandResult.Succeeded(
                normalizedName,
                FormatCommandList(arguments.FirstOrDefault())),
            "/version" => SlashCommandResult.Succeeded("/version", FormatVersion()),
            "/update" => await RunUpdateCommandAsync(arguments, argumentText, cancellationToken),
            "/download" => await DownloadUpdateAsync(cancellationToken),
            "/restart" => SlashCommandResult.Succeeded("/restart", FormatRestartPlan(argumentText)),
            "/readiness" => SlashCommandResult.Succeeded("/readiness", await FormatReadinessAsync(cancellationToken)),
            "/status" => SlashCommandResult.Succeeded("/status", await FormatStatusAsync(cancellationToken)),
            "/model" => SlashCommandResult.Succeeded("/model", FormatModelStatus()),
            "/limits" => SlashCommandResult.Succeeded("/limits", FormatLimitWarnings()),
            "/permissions" => SlashCommandResult.Succeeded("/permissions", FormatPermissions()),
            "/settings" => SlashCommandResult.Succeeded("/settings", "Opening Settings."),
            "/integrations" => SlashCommandResult.Succeeded("/integrations", "Opening Integrations."),
            "/diagnostics" => SlashCommandResult.Succeeded("/diagnostics", "Opening Runtime Diagnostics."),
            "/coordinator" => SlashCommandResult.Succeeded("/coordinator", "Opening Coordinator."),
            "/theme" => SlashCommandResult.Succeeded("/theme", "Theme toggled."),
            "/voice" => SlashCommandResult.Succeeded("/voice", "Voice capture toggled."),
            "/diff" => SlashCommandResult.Succeeded("/diff", FormatCodingAgentWorkflow("/diff")),
            "/dependabot" => SlashCommandResult.Succeeded("/dependabot", FormatCodingAgentWorkflow("/dependabot")),
            "/github" => await RunGitHubCommandAsync(arguments, cancellationToken),
            "/github-app" => await RunGitHubAppCommandAsync(arguments, cancellationToken),
            "/plugins" => SlashCommandResult.Succeeded("/plugins", await FormatPluginsAsync(cancellationToken)),
            "/mcp" => SlashCommandResult.Succeeded("/mcp", FormatMcp()),
            "/agent" => await RunAgentCommandAsync(argumentText, cancellationToken),
            "/agents" => RunAgentsCommand(arguments),
            "/test" => await RunSkillTestAsync(arguments, cancellationToken),
            "/review" => SlashCommandResult.Succeeded("/review", await FormatReviewAsync(cancellationToken)),
            "/worktree" => SlashCommandResult.Succeeded("/worktree", FormatCodingAgentWorkflow("/worktree")),
            "/hooks" => SlashCommandResult.Succeeded("/hooks", FormatCodingAgentWorkflow("/hooks")),
            "/clear" => SlashCommandResult.Succeeded("/clear", "Input cleared."),
            _ => SlashCommandResult.Failed(commandName, $"Unknown slash command: {commandName}")
        };
    }

    private async Task<string> FormatStatusAsync(CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .AppendLine("Kam status:")
            .AppendLine($"  host: {(_hostControl?.IsRunning == false ? "stopped" : "running")}");

        if (_skillHealthService is null)
        {
            return builder
                .AppendLine("  skills: health service unavailable")
                .ToString()
                .TrimEnd();
        }

        var reports = await _skillHealthService.GetHealthAsync(cancellationToken);
        builder.AppendLine($"  skills: {reports.Count} registered");
        foreach (var group in reports.GroupBy(report => report.Status).OrderBy(group => group.Key.ToString()))
        {
            builder.AppendLine($"  {group.Key}: {group.Count()}");
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<string> FormatReadinessAsync(CancellationToken cancellationToken)
    {
        var checks = new List<(string Name, bool Ready, string Detail, string NextAction)>();

        var modelReady = !string.IsNullOrWhiteSpace(_aiServiceConfiguration.Provider)
            && !string.IsNullOrWhiteSpace(_aiServiceConfiguration.ModelId)
            && (!RequiresApiKey(_aiServiceConfiguration.Provider) || !string.IsNullOrWhiteSpace(_aiServiceConfiguration.ApiKey));
        checks.Add((
            "model",
            modelReady,
            modelReady
                ? $"ready ({NormalizeMessage(_aiServiceConfiguration.Provider)} / {NormalizeMessage(_aiServiceConfiguration.ModelId)})"
                : "missing provider, model, or required API key",
            "Configure an AI runtime provider and model."));

        IReadOnlyCollection<SkillHealthReport>? healthReports = null;
        if (_skillHealthService is not null)
        {
            healthReports = await _skillHealthService.GetHealthAsync(cancellationToken);
        }

        var skillReady = healthReports is { Count: > 0 }
            && healthReports.All(report => report.Status == SkillHealthStatus.Healthy);
        checks.Add((
            "skills",
            skillReady,
            healthReports is null
                ? "health service is not registered"
                : $"{healthReports.Count} checked, {healthReports.Count(report => report.Status != SkillHealthStatus.Healthy)} need attention",
            "Open Skills and resolve unhealthy or permission-blocked tools."));

        var agentRuntimeReady = _skillExecutionPipeline is not null && _runtimeAgentRunStore is not null;
        checks.Add((
            "agent runtime",
            agentRuntimeReady,
            agentRuntimeReady ? "ready" : "Task agent runtime is not registered.",
            "Restart Kam or review runtime service registration."));

        var voiceReady = _hostControl?.IsRunning != false;
        checks.Add((
            "voice host",
            voiceReady,
            voiceReady ? "running or ready to start" : "stopped",
            "Start voice capture from the command center."));

        var requiredChecksReady = checks
            .Where(check => check.Name is "model" or "skills" or "agent runtime")
            .All(check => check.Ready);

        var builder = new StringBuilder()
            .AppendLine($"Kam readiness: {(requiredChecksReady ? "READY" : "NEEDS_ACTION")}");

        foreach (var check in checks)
        {
            builder.AppendLine($"  {check.Name}: {(check.Ready ? "ready" : "needs action")} - {check.Detail}");
        }

        var nextActions = checks
            .Where(check => !check.Ready)
            .Select(check => check.NextAction)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (nextActions.Length > 0)
        {
            builder.AppendLine("  next actions:");
            foreach (var action in nextActions)
            {
                builder.AppendLine($"    - {action}");
            }

            builder.AppendLine("    - Open Settings > AI Runtime when model configuration is missing.");
        }
        else
        {
            builder.AppendLine("  next action: run a real command or open Runtime Diagnostics for live evidence.");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool RequiresApiKey(string provider)
    {
        return !provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
            && !provider.Equals("Local", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPermissions()
    {
        return string.Join(Environment.NewLine, [
            "Kam chat slash-command permissions:",
            "  commands: app-local command registry only",
            "  shell: not available from chat slash commands",
            "  files: no direct file mutation from chat slash commands",
            "  tests: limited to registered skill smoke tests",
            "  integrations: status output redacts secrets"
        ]);
    }

    private string FormatModelStatus()
    {
        return string.Join(Environment.NewLine, [
            "Kam AI runtime model:",
            $"  provider: {FormatConfiguredValue(_aiServiceConfiguration.Provider)}",
            $"  endpoint: {FormatConfiguredValue(_aiServiceConfiguration.Endpoint)}",
            $"  model: {FormatConfiguredValue(_aiServiceConfiguration.ModelId)}",
            "  scope: selected runtime profile is mapped into planner, chat, skills, and agents at startup",
            "  changes: validate settings, then restart Kam to rebuild active AI clients"
        ]);
    }

    private static string FormatLimitWarnings()
    {
        return string.Join(Environment.NewLine, [
            "Kam provider-limit warnings:",
            "  rate limit: activity log warns when provider returns 429 or rate-limit text",
            "  quota/balance: activity log warns when billing, quota, balance, or credit errors are detected",
            "  authentication: activity log warns when a provider rejects the configured API key",
            "  action: switch model/provider, wait for reset, or update billing/API key in Settings"
        ]);
    }

    private string FormatVersion()
    {
        return string.Join(Environment.NewLine, [
            "Kam version:",
            $"  current: {CurrentApplicationVersion()}",
            "  release feed: GitHub Releases"
        ]);
    }

    private async Task<SlashCommandResult> RunUpdateCommandAsync(
        IReadOnlyList<string> arguments,
        string argumentText,
        CancellationToken cancellationToken)
    {
        var action = arguments.FirstOrDefault()?.ToLowerInvariant() ?? "check";
        return action switch
        {
            "check" or "status" => await CheckUpdateAsync(cancellationToken),
            "download" => await DownloadUpdateAsync(cancellationToken),
            "restart" => SlashCommandResult.Succeeded(
                "/update",
                FormatRestartPlan(GetArgumentTextAfterFirstToken(argumentText))),
            _ => SlashCommandResult.Failed("/update", "Usage: /update [check|download|restart]")
        };
    }

    private async Task<SlashCommandResult> CheckUpdateAsync(CancellationToken cancellationToken)
    {
        if (_applicationUpdateService is null)
        {
            return SlashCommandResult.Failed("/update", "Application update service is unavailable.");
        }

        var update = await _applicationUpdateService.CheckForUpdatesAsync(cancellationToken);
        _applicationUpdateSession.RecordCheck(update);
        if (!update.Success)
        {
            return SlashCommandResult.Failed("/update", update.Message);
        }

        var lines = new List<string>
        {
            "Kam update status:",
            $"  current: {update.CurrentVersion}",
            $"  latest: {update.LatestVersion ?? "(unknown)"}",
            $"  status: {(update.IsUpdateAvailable ? "update available" : "up to date")}"
        };

        if (!string.IsNullOrWhiteSpace(update.ReleaseName))
        {
            lines.Add($"  release: {update.ReleaseName}");
        }

        if (!string.IsNullOrWhiteSpace(update.ReleaseUrl))
        {
            lines.Add($"  url: {update.ReleaseUrl}");
        }

        if (update.Asset is not null)
        {
            lines.Add($"  asset: {update.Asset.Name} ({FormatBytes(update.Asset.SizeBytes)})");
            lines.Add($"  checksum: {(string.IsNullOrWhiteSpace(update.Asset.ChecksumDownloadUrl) ? "missing" : update.Asset.ChecksumName ?? "available")}");
            lines.Add("  next: /download");
        }
        else if (update.IsUpdateAvailable)
        {
            lines.Add("  download: no release package asset found");
            lines.Add("  next: wait for a packaged Kam release asset");
        }

        return SlashCommandResult.Succeeded("/update", string.Join(Environment.NewLine, lines));
    }

    private async Task<SlashCommandResult> DownloadUpdateAsync(CancellationToken cancellationToken)
    {
        if (_applicationUpdateService is null)
        {
            return SlashCommandResult.Failed("/download", "Application update service is unavailable.");
        }

        var download = await _applicationUpdateService.DownloadLatestAsync(cancellationToken);
        if (!download.Success)
        {
            _applicationUpdateSession.ClearDownload();
            return SlashCommandResult.Failed("/download", download.Message);
        }

        _applicationUpdateSession.RecordDownload(download);

        var nextStep = download.IsVerified
            ? "next: /restart <file>"
            : "next: verify release package before restart";
        var restartStatus = download.IsVerified
            ? "restart: ready after verified package review"
            : "restart: blocked until package verification succeeds";

        return SlashCommandResult.Succeeded(
            "/download",
            string.Join(Environment.NewLine, [
                "Kam update downloaded:",
                $"  version: {download.Version ?? "(unknown)"}",
                $"  file: {download.FilePath}",
                $"  size: {FormatBytes(download.SizeBytes ?? 0)}",
                $"  verification: {download.VerificationStatus}",
                $"  {restartStatus}",
                $"  {nextStep}"
            ]));
    }

    private string FormatRestartPlan(string? updatePackagePath)
    {
        if (_applicationRestartPlanner is null)
        {
            return "Kam restart planner is unavailable.";
        }

        var normalizedPackagePath = NormalizePathArgument(updatePackagePath);
        var validation = _applicationUpdateSession.ValidateRestartPackage(normalizedPackagePath);
        if (!validation.CanRestart)
        {
            return FormatBlockedRestartPlan(validation.NormalizedPackagePath ?? normalizedPackagePath, validation.Message);
        }

        var plan = _applicationRestartPlanner.CreateRestartPlan(
            string.IsNullOrWhiteSpace(normalizedPackagePath)
                ? null
                : validation.NormalizedPackagePath ?? normalizedPackagePath);
        var lines = new List<string>
        {
            "Kam restart plan:",
            $"  status: {(plan.CanRestart ? "ready" : "manual action required")}",
            $"  message: {plan.Message}"
        };

        if (!string.IsNullOrWhiteSpace(plan.UpdatePackagePath))
        {
            lines.Add($"  package: {plan.UpdatePackagePath}");
        }

        if (!string.IsNullOrWhiteSpace(plan.ExecutablePath))
        {
            lines.Add($"  executable: {plan.ExecutablePath}");
        }

        foreach (var step in plan.Steps)
        {
            lines.Add($"  - {step}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatBlockedRestartPlan(string? updatePackagePath, string message)
    {
        var lines = new List<string>
        {
            "Kam restart plan:",
            "  status: blocked",
            $"  message: {message}",
            "  verification: run /download successfully, then pass the verified package path to /restart"
        };

        if (!string.IsNullOrWhiteSpace(updatePackagePath))
        {
            lines.Insert(3, $"  package: {updatePackagePath}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string CurrentApplicationVersion()
    {
        return _applicationVersionProvider?.CurrentVersion
            ?? _applicationUpdateService?.CurrentVersion
            ?? "(unavailable)";
    }

    private async Task<string> FormatPluginsAsync(CancellationToken cancellationToken)
    {
        if (_skillHealthService is null)
        {
            return "Kam plugins: skill health service unavailable.";
        }

        var reports = await _skillHealthService.GetHealthAsync(cancellationToken);
        var builder = new StringBuilder()
            .AppendLine("Kam plugins/skills:");

        foreach (var group in reports.GroupBy(report => report.Status).OrderBy(group => group.Key.ToString()))
        {
            builder.AppendLine($"  {group.Key}: {group.Count()}");
        }

        var attention = reports
            .Where(report => report.Status != SkillHealthStatus.Healthy)
            .OrderBy(report => report.SkillId, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        if (attention.Length == 0)
        {
            builder.AppendLine("  all registered skills are healthy");
        }
        else
        {
            builder.AppendLine("  attention:");
            foreach (var report in attention)
            {
                builder.AppendLine($"    {report.SkillId}: {report.Status}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string FormatMcp()
    {
        return string.Join(Environment.NewLine, [
            "Kam MCP status:",
            $"  Todoist endpoint: {FormatConfiguredValue(_mcpOptions.TodoistServerLink)}",
            $"  Todoist API key: {FormatSecretStatus(_mcpOptions.TodoistApiKey)}",
            "  MCP commands in chat are status-only"
        ]);
    }

    private async Task<SlashCommandResult> RunAgentCommandAsync(
        string argumentText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argumentText))
        {
            return SlashCommandResult.Failed("/agent", "Usage: /agent <task>");
        }

        if (_skillExecutionPipeline is null)
        {
            return SlashCommandResult.Failed("/agent", "Task agent runtime is unavailable.");
        }

        var result = await _skillExecutionPipeline.ExecuteAsync(
            SkillPlan.FromObject(
                "agents.run",
                new
                {
                    task = argumentText,
                    role = "general",
                    agentName = "TaskAgent"
                }),
            cancellationToken);

        return result.Success
            ? SlashCommandResult.Succeeded("/agent", result.Message)
            : SlashCommandResult.Failed("/agent", result.ErrorMessage);
    }

    private SlashCommandResult RunAgentsCommand(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return SlashCommandResult.Succeeded("/agents", FormatAgents());
        }

        if (_runtimeAgentRunStore is null)
        {
            return SlashCommandResult.Failed("/agents", "Runtime agent run store is unavailable.");
        }

        if (IsCommand(arguments[0], "cancel", "stop"))
        {
            if (arguments.Count < 2)
            {
                return SlashCommandResult.Failed("/agents", "Usage: /agents cancel <runId>");
            }

            var run = _runtimeAgentRunStore.Get(arguments[1]);
            if (run is null)
            {
                return SlashCommandResult.Failed("/agents", $"Runtime agent run '{NormalizeMessage(arguments[1])}' was not found.");
            }

            var canceled = _runtimeAgentRunStore.Cancel(run.RunId, "Canceled by user from slash command.");
            return SlashCommandResult.Succeeded(
                "/agents",
                string.Join(Environment.NewLine, [
                    "Kam agent run canceled:",
                    $"  run: {NormalizeMessage(canceled.RunId)}",
                    $"  agent: {NormalizeMessage(canceled.AgentName)}",
                    $"  status: {canceled.Status}"
                ]));
        }

        if (IsCommand(arguments[0], "retry", "rerun"))
        {
            if (arguments.Count < 2)
            {
                return SlashCommandResult.Failed("/agents", "Usage: /agents retry <runId>");
            }

            var run = _runtimeAgentRunStore.Get(arguments[1]);
            if (run is null)
            {
                return SlashCommandResult.Failed("/agents", $"Runtime agent run '{NormalizeMessage(arguments[1])}' was not found.");
            }

            if (run.Status == RuntimeAgentRunStatus.Running)
            {
                return SlashCommandResult.Failed("/agents", "Runtime agent run is still running; cancel it before retrying.");
            }

            var retry = _runtimeAgentRunStore.Start(
                new RuntimeAgentRequest(
                    run.AgentName,
                    run.Role,
                    run.Task,
                    run.ToolObservations),
                run.ModelId);
            return SlashCommandResult.Succeeded(
                "/agents",
                string.Join(Environment.NewLine, [
                    "Kam agent retry queued:",
                    $"  run: {NormalizeMessage(retry.RunId)}",
                    $"  from: {NormalizeMessage(run.RunId)}",
                    $"  agent: {NormalizeMessage(retry.AgentName)}",
                    $"  model: {NormalizeMessage(retry.ModelId)}"
                ]));
        }

        return SlashCommandResult.Failed("/agents", "Usage: /agents [cancel|retry] <runId>");
    }

    private string FormatAgents()
    {
        var runs = _runtimeAgentRunStore?.List(12) ?? [];
        var names = _agentRegistry?.GetAllAgentNames()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (names.Length == 0 && runs.Count == 0)
        {
            return "Kam agents: no runtime agents are registered yet.";
        }

        var builder = new StringBuilder()
            .AppendLine("Kam agents:");

        if (runs.Count > 0)
        {
            builder.AppendLine("  task runs:");
            foreach (var run in runs)
            {
                builder.AppendLine(
                    $"    {run.AgentName} [{run.Status}] {FormatRuntimeAgentRunAge(run)} - {NormalizeMessage(run.LastMessage ?? run.Role)}");
            }
        }

        if (names.Length > 0)
        {
            builder.AppendLine("  registered agents:");
            foreach (var name in names)
            {
                builder.AppendLine($"    {name}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatRuntimeAgentRunAge(RuntimeAgentRun run)
    {
        var reference = run.CompletedAt ?? DateTimeOffset.UtcNow;
        var elapsed = reference - run.StartedAt;
        return elapsed.TotalSeconds < 1
            ? "just now"
            : $"{Math.Max(1, (int)Math.Round(elapsed.TotalSeconds))}s";
    }

    private string FormatCodingAgentWorkflow(string commandName)
    {
        var workspace = _codingAgentOptions.GetWorkspaceRootOrDefault() ?? "(not configured)";
        return string.Join(Environment.NewLine, [
            $"Kam {commandName}:",
            "  available in coding-agent mode",
            $"  workspace: {workspace}",
            $"  approvalMode: {_codingAgentOptions.ApprovalMode}",
            "  chat slash commands do not run shell, git, or gh workflows directly",
            $"  run from CLI: kam coding-agent {commandName}"
        ]);
    }

    private string FormatGitHubContext()
    {
        if (_activeGitHubWorkflowRun is null && _activeGitHubPullRequest is null)
        {
            RestoreApplicationSessionContext();
        }

        var builder = new StringBuilder()
            .AppendLine("Kam GitHub context:")
            .AppendLine($"  repository: {NormalizeMessage(_activeGitHubRepositoryFullName)}");

        if (_activeGitHubWorkflowRun is null)
        {
            builder.AppendLine("  workflow run: (none)");
        }
        else
        {
            builder.AppendLine(
                $"  workflow run: {NormalizeMessage(_activeGitHubWorkflowRun.RepositoryFullName)}#{_activeGitHubWorkflowRun.Id} {NormalizeMessage(_activeGitHubWorkflowRun.Name)} ({FormatWorkflowRunState(_activeGitHubWorkflowRun)})");
        }

        if (_activeGitHubPullRequest is null)
        {
            builder.AppendLine("  pull request: (none)");
        }
        else
        {
            builder
                .AppendLine(
                    $"  pull request: {NormalizeMessage(_activeGitHubPullRequest.RepositoryFullName)}#{_activeGitHubPullRequest.Number} {NormalizeMessage(_activeGitHubPullRequest.Title)}")
                .AppendLine(
                    $"  branch: {NormalizeMessage(_activeGitHubPullRequest.HeadRefName)} -> {NormalizeMessage(_activeGitHubPullRequest.BaseRefName)}");
        }

        builder.AppendLine("  follow-up: /github fix can use the active workflow run without repeated arguments");
        return builder.ToString().TrimEnd();
    }

    private void RestoreApplicationSessionContext()
    {
        var gitHub = _applicationSessionContextStore?.Load().GitHub;
        if (gitHub is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(gitHub.RepositoryFullName))
        {
            _activeGitHubRepositoryFullName = gitHub.RepositoryFullName;
        }

        if (gitHub.ActiveWorkflowRun is not null)
        {
            _activeGitHubWorkflowRun = MapWorkflowRun(gitHub.ActiveWorkflowRun);
            _activeGitHubRepositoryFullName = _activeGitHubWorkflowRun.RepositoryFullName;
        }

        if (gitHub.ActivePullRequest is not null)
        {
            _activeGitHubPullRequest = MapPullRequest(gitHub.ActivePullRequest);
            _activeGitHubRepositoryFullName = _activeGitHubPullRequest.RepositoryFullName;
        }
    }

    private async Task<SlashCommandResult> RunGitHubCommandAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Count > 0 && IsCommand(arguments[0], "app", "status"))
        {
            if (arguments.Count > 1 && IsCommand(arguments[1], "repos", "repositories", "list"))
            {
                return await RunGitHubAppRepositoriesAsync("/github", cancellationToken);
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "prs", "pr", "pulls", "pull-requests"))
            {
                return await RunGitHubAppPullRequestsAsync("/github", cancellationToken);
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "actions", "runs", "workflows", "workflow-runs"))
            {
                return await RunGitHubAppWorkflowRunsAsync("/github", cancellationToken);
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "context", "ctx", "selected"))
            {
                return SlashCommandResult.Succeeded("/github", FormatGitHubContext());
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "diagnose", "doctor", "ci"))
            {
                return await RunGitHubAppWorkflowDiagnosisAsync(
                    "/github",
                    arguments,
                    2,
                    cancellationToken);
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "fix", "autofix", "auto-fix"))
            {
                return await RunGitHubAppWorkflowAutoFixAsync(
                    "/github",
                    arguments,
                    2,
                    cancellationToken);
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "logs", "log"))
            {
                return await RunGitHubAppWorkflowJobLogsAsync(
                    "/github",
                    arguments,
                    2,
                    cancellationToken);
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "run", "jobs"))
            {
                return await RunGitHubAppWorkflowRunJobsAsync(
                    "/github",
                    arguments,
                    2,
                    cancellationToken);
            }

            return await RunGitHubAppStatusAsync("/github", cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "repos", "repositories", "list"))
        {
            return await RunGitHubAppRepositoriesAsync("/github", cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "prs", "pr", "pulls", "pull-requests"))
        {
            return await RunGitHubAppPullRequestsAsync("/github", cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "actions", "runs", "workflows", "workflow-runs"))
        {
            return await RunGitHubAppWorkflowRunsAsync("/github", cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "context", "ctx", "selected"))
        {
            return SlashCommandResult.Succeeded("/github", FormatGitHubContext());
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "diagnose", "doctor", "ci"))
        {
            return await RunGitHubAppWorkflowDiagnosisAsync(
                "/github",
                arguments,
                1,
                cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "fix", "autofix", "auto-fix"))
        {
            return await RunGitHubAppWorkflowAutoFixAsync(
                "/github",
                arguments,
                1,
                cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "logs", "log"))
        {
            return await RunGitHubAppWorkflowJobLogsAsync(
                "/github",
                arguments,
                1,
                cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "run", "jobs"))
        {
            return await RunGitHubAppWorkflowRunJobsAsync(
                "/github",
                arguments,
                1,
                cancellationToken);
        }

        return SlashCommandResult.Succeeded(
            "/github",
            string.Join(Environment.NewLine, [
                FormatCodingAgentWorkflow("/github"),
                "  app status: /github app",
                "  workflow runs: /github actions",
                "  active context: /github context",
                "  diagnose workflow run: /github diagnose <owner/repo> [runId]",
                "  auto-fix workflow run: /github fix <owner/repo> [runId]",
                "  workflow job logs: /github logs <owner/repo> <jobId>",
                "  workflow run jobs: /github run <owner/repo> <runId>",
                "  pull requests: /github prs",
                "  repo list: /github repos"
            ]));
    }

    private async Task<SlashCommandResult> RunGitHubAppCommandAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Count > 0 && IsCommand(arguments[0], "repos", "repositories", "list"))
        {
            return await RunGitHubAppRepositoriesAsync("/github-app", cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "prs", "pr", "pulls", "pull-requests"))
        {
            return await RunGitHubAppPullRequestsAsync("/github-app", cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "actions", "runs", "workflows", "workflow-runs"))
        {
            return await RunGitHubAppWorkflowRunsAsync("/github-app", cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "context", "ctx", "selected"))
        {
            return SlashCommandResult.Succeeded("/github-app", FormatGitHubContext());
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "diagnose", "doctor", "ci"))
        {
            return await RunGitHubAppWorkflowDiagnosisAsync(
                "/github-app",
                arguments,
                1,
                cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "fix", "autofix", "auto-fix"))
        {
            return await RunGitHubAppWorkflowAutoFixAsync(
                "/github-app",
                arguments,
                1,
                cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "logs", "log"))
        {
            return await RunGitHubAppWorkflowJobLogsAsync(
                "/github-app",
                arguments,
                1,
                cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "run", "jobs"))
        {
            return await RunGitHubAppWorkflowRunJobsAsync(
                "/github-app",
                arguments,
                1,
                cancellationToken);
        }

        return await RunGitHubAppStatusAsync("/github-app", cancellationToken);
    }

    private async Task<SlashCommandResult> RunGitHubAppStatusAsync(
        string commandName,
        CancellationToken cancellationToken)
    {
        if (_githubAppClient is null)
        {
            return SlashCommandResult.Succeeded(commandName, FormatGitHubAppUnavailable());
        }

        var status = await _githubAppClient.GetStatusAsync(cancellationToken);
        return SlashCommandResult.Succeeded(commandName, FormatGitHubAppStatus(status));
    }

    private async Task<SlashCommandResult> RunGitHubAppRepositoriesAsync(
        string commandName,
        CancellationToken cancellationToken)
    {
        if (_githubAppClient is null)
        {
            return SlashCommandResult.Succeeded(commandName, FormatGitHubAppUnavailable());
        }

        var result = await _githubAppClient.ListRepositoriesAsync(cancellationToken);
        if (!result.Success)
        {
            return SlashCommandResult.Succeeded(
                commandName,
                FormatGitHubAppSetup(result.Message, result.MissingSettings));
        }

        var builder = new StringBuilder()
            .AppendLine("Kam GitHub App repositories:")
            .AppendLine($"  status: {NormalizeMessage(result.Message)}");

        foreach (var repository in result.Repositories
            .OrderBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(50))
        {
            builder.AppendLine(
                $"  - {NormalizeMessage(repository.FullName)} ({(repository.IsPrivate ? "private" : "public")}, default: {NormalizeMessage(repository.DefaultBranch)})");
        }

        if (result.Repositories.Count > 50)
        {
            builder.AppendLine($"  ... {result.Repositories.Count - 50} more repositories hidden");
        }

        return SlashCommandResult.Succeeded(commandName, builder.ToString().TrimEnd());
    }

    private async Task<SlashCommandResult> RunGitHubAppWorkflowRunsAsync(
        string commandName,
        CancellationToken cancellationToken)
    {
        if (_githubAppClient is null)
        {
            return SlashCommandResult.Succeeded(commandName, FormatGitHubAppUnavailable());
        }

        var result = await _githubAppClient.ListWorkflowRunsAsync(cancellationToken);
        if (!result.Success)
        {
            return SlashCommandResult.Succeeded(
                commandName,
                FormatGitHubAppSetup(result.Message, result.MissingSettings));
        }

        var builder = new StringBuilder()
            .AppendLine("Kam GitHub App workflow runs:")
            .AppendLine($"  status: {NormalizeMessage(result.Message)}");

        var activeRun = SelectWorkflowRunForContext(result.WorkflowRuns);
        if (activeRun is not null)
        {
            SetActiveGitHubWorkflowRun(activeRun);
            builder.AppendLine($"  active run: {NormalizeMessage(activeRun.RepositoryFullName)}#{activeRun.Id}");
        }

        foreach (var workflowRun in result.WorkflowRuns
            .OrderByDescending(workflowRun => workflowRun.UpdatedAt ?? workflowRun.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(workflowRun => workflowRun.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(workflowRun => workflowRun.Id)
            .Take(50))
        {
            builder
                .AppendLine(
                    $"  - {NormalizeMessage(workflowRun.RepositoryFullName)}#{workflowRun.Id} {NormalizeMessage(workflowRun.Name)} ({FormatWorkflowRunState(workflowRun)}, {NormalizeMessage(workflowRun.Event)}, {NormalizeMessage(workflowRun.HeadBranch)})")
                .AppendLine($"    title: {NormalizeMessage(workflowRun.DisplayTitle)}")
                .AppendLine($"    {NormalizeMessage(workflowRun.HtmlUrl)}");
        }

        if (result.WorkflowRuns.Count == 0)
        {
            builder.AppendLine("  no workflow runs found");
        }
        else if (result.WorkflowRuns.Count > 50)
        {
            builder.AppendLine($"  ... {result.WorkflowRuns.Count - 50} more workflow runs hidden");
        }

        return SlashCommandResult.Succeeded(commandName, builder.ToString().TrimEnd());
    }

    private async Task<SlashCommandResult> RunGitHubAppWorkflowDiagnosisAsync(
        string commandName,
        IReadOnlyList<string> arguments,
        int firstArgumentIndex,
        CancellationToken cancellationToken)
    {
        if (arguments.Count <= firstArgumentIndex)
        {
            return SlashCommandResult.Failed(
                commandName,
                "Usage: /github diagnose <owner/repo> [runId]");
        }

        long? requestedRunId = null;
        if (arguments.Count > firstArgumentIndex + 1)
        {
            if (!long.TryParse(arguments[firstArgumentIndex + 1], out var runId) || runId <= 0)
            {
                return SlashCommandResult.Failed(
                    commandName,
                    "Usage: /github diagnose <owner/repo> [runId]");
            }

            requestedRunId = runId;
        }

        if (_githubAppClient is null)
        {
            return SlashCommandResult.Succeeded(commandName, FormatGitHubAppUnavailable());
        }

        var repositoryFullName = arguments[firstArgumentIndex];
        GitHubWorkflowRunSummary? selectedRun = null;
        string? selectionMessage = null;
        var runIdToDiagnose = requestedRunId;
        if (runIdToDiagnose is null)
        {
            var workflowRuns = await _githubAppClient.ListWorkflowRunsAsync(cancellationToken);
            if (!workflowRuns.Success)
            {
                return SlashCommandResult.Succeeded(
                    commandName,
                    FormatGitHubAppSetup(workflowRuns.Message, workflowRuns.MissingSettings));
            }

            selectedRun = SelectWorkflowRunForDiagnosis(workflowRuns.WorkflowRuns, repositoryFullName);
            if (selectedRun is null)
            {
                return SlashCommandResult.Succeeded(
                    commandName,
                    $"Kam GitHub CI doctor:{Environment.NewLine}  repository: {NormalizeMessage(repositoryFullName)}{Environment.NewLine}  no workflow runs found for this repository");
            }

            runIdToDiagnose = selectedRun.Id;
            selectionMessage = IsUnhealthyWorkflowRun(selectedRun)
                ? "latest failing or in-progress workflow run"
                : "latest workflow run";
        }

        var jobs = await _githubAppClient.ListWorkflowRunJobsAsync(
            repositoryFullName,
            runIdToDiagnose.Value,
            cancellationToken);
        if (!jobs.Success)
        {
            return SlashCommandResult.Succeeded(
                commandName,
                FormatGitHubAppSetup(jobs.Message, jobs.MissingSettings));
        }

        return SlashCommandResult.Succeeded(
            commandName,
            FormatWorkflowDiagnosis(jobs, selectedRun, selectionMessage));
    }

    private async Task<SlashCommandResult> RunGitHubAppWorkflowAutoFixAsync(
        string commandName,
        IReadOnlyList<string> arguments,
        int firstArgumentIndex,
        CancellationToken cancellationToken)
    {
        var repositoryFullName = arguments.Count > firstArgumentIndex
            ? arguments[firstArgumentIndex]
            : _activeGitHubWorkflowRun?.RepositoryFullName;
        long? requestedRunId = null;
        if (arguments.Count > firstArgumentIndex + 1)
        {
            if (!long.TryParse(arguments[firstArgumentIndex + 1], out var runId) || runId <= 0)
            {
                return SlashCommandResult.Failed(
                    commandName,
                    "Usage: /github fix <owner/repo> [runId]");
            }

            requestedRunId = runId;
        }
        else if (arguments.Count <= firstArgumentIndex && _activeGitHubWorkflowRun is not null)
        {
            requestedRunId = _activeGitHubWorkflowRun.Id;
        }

        if (string.IsNullOrWhiteSpace(repositoryFullName))
        {
            return SlashCommandResult.Failed(
                commandName,
                "Usage: /github fix <owner/repo> [runId]. Run /github actions first to set active workflow context.");
        }

        if (_githubAppClient is null)
        {
            return SlashCommandResult.Succeeded(commandName, FormatGitHubAppUnavailable());
        }

        if (_skillExecutionPipeline is null)
        {
            return SlashCommandResult.Failed(commandName, "Task agent runtime is unavailable.");
        }

        GitHubWorkflowRunSummary? selectedRun = null;
        string? selectionMessage = null;
        var runIdToDiagnose = requestedRunId;
        var activeWorkflowRun = _activeGitHubWorkflowRun;
        if (activeWorkflowRun is not null
            && runIdToDiagnose == activeWorkflowRun.Id
            && repositoryFullName.Equals(activeWorkflowRun.RepositoryFullName, StringComparison.OrdinalIgnoreCase))
        {
            selectedRun = activeWorkflowRun;
            selectionMessage = "active workflow context";
        }

        if (runIdToDiagnose is null)
        {
            var workflowRuns = await _githubAppClient.ListWorkflowRunsAsync(cancellationToken);
            if (!workflowRuns.Success)
            {
                return SlashCommandResult.Succeeded(
                    commandName,
                    FormatGitHubAppSetup(workflowRuns.Message, workflowRuns.MissingSettings));
            }

            selectedRun = SelectWorkflowRunForDiagnosis(workflowRuns.WorkflowRuns, repositoryFullName);
            if (selectedRun is null)
            {
                return SlashCommandResult.Succeeded(
                    commandName,
                    $"Kam GitHub CI auto-fix:{Environment.NewLine}  repository: {NormalizeMessage(repositoryFullName)}{Environment.NewLine}  no workflow runs found for this repository");
            }

            runIdToDiagnose = selectedRun.Id;
            selectionMessage = IsUnhealthyWorkflowRun(selectedRun)
                ? "latest failing or in-progress workflow run"
                : "latest workflow run";
        }

        var jobs = await _githubAppClient.ListWorkflowRunJobsAsync(
            repositoryFullName,
            runIdToDiagnose.Value,
            cancellationToken);
        if (!jobs.Success)
        {
            return SlashCommandResult.Succeeded(
                commandName,
                FormatGitHubAppSetup(jobs.Message, jobs.MissingSettings));
        }

        var selectedJob = SelectWorkflowJobForAutoFix(jobs.Jobs);
        if (selectedJob is null)
        {
            return SlashCommandResult.Succeeded(
                commandName,
                $"Kam GitHub CI auto-fix:{Environment.NewLine}  run: {NormalizeMessage(jobs.RepositoryFullName)}#{jobs.RunId}{Environment.NewLine}  no failing or active jobs found for this workflow run");
        }

        var log = await _githubAppClient.GetWorkflowJobLogAsync(
            repositoryFullName,
            selectedJob.Id,
            cancellationToken);
        if (!log.Success)
        {
            return SlashCommandResult.Succeeded(
                commandName,
                FormatGitHubAppSetup(log.Message, log.MissingSettings));
        }

        var runtimeTask = BuildGitHubAutoFixRuntimeTask(
            jobs,
            selectedRun,
            selectionMessage,
            selectedJob,
            log);
        var runtimeResult = await _skillExecutionPipeline.ExecuteAsync(
            SkillPlan.FromObject(
                "agents.run",
                new
                {
                    task = runtimeTask,
                    role = "coding",
                    agentName = "GitHubFixAgent"
                }),
            cancellationToken);

        var builder = new StringBuilder()
            .AppendLine("Kam GitHub CI auto-fix:")
            .AppendLine($"  run: {NormalizeMessage(jobs.RepositoryFullName)}#{jobs.RunId}");

        if (!string.IsNullOrWhiteSpace(selectionMessage))
        {
            builder.AppendLine($"  selected: {NormalizeMessage(selectionMessage)}");
        }

        builder
            .AppendLine($"  job: {NormalizeMessage(selectedJob.Name)} ({FormatWorkflowJobState(selectedJob)})")
            .AppendLine($"  result: {NormalizeMessage(runtimeResult.Success ? runtimeResult.Message : runtimeResult.ErrorMessage)}")
            .AppendLine("  approval: patch and test actions remain approval-gated");

        return runtimeResult.Success
            ? SlashCommandResult.Succeeded(commandName, builder.ToString().TrimEnd())
            : SlashCommandResult.Failed(commandName, builder.ToString().TrimEnd());
    }

    private async Task<SlashCommandResult> RunGitHubAppWorkflowJobLogsAsync(
        string commandName,
        IReadOnlyList<string> arguments,
        int firstArgumentIndex,
        CancellationToken cancellationToken)
    {
        if (arguments.Count <= firstArgumentIndex + 1)
        {
            return SlashCommandResult.Failed(
                commandName,
                "Usage: /github logs <owner/repo> <jobId>");
        }

        if (!long.TryParse(arguments[firstArgumentIndex + 1], out var jobId) || jobId <= 0)
        {
            return SlashCommandResult.Failed(
                commandName,
                "Usage: /github logs <owner/repo> <jobId>");
        }

        if (_githubAppClient is null)
        {
            return SlashCommandResult.Succeeded(commandName, FormatGitHubAppUnavailable());
        }

        var repositoryFullName = arguments[firstArgumentIndex];
        var result = await _githubAppClient.GetWorkflowJobLogAsync(
            repositoryFullName,
            jobId,
            cancellationToken);
        if (!result.Success)
        {
            return SlashCommandResult.Succeeded(
                commandName,
                FormatGitHubAppSetup(result.Message, result.MissingSettings));
        }

        return SlashCommandResult.Succeeded(commandName, FormatWorkflowJobLogs(result));
    }

    private async Task<SlashCommandResult> RunGitHubAppWorkflowRunJobsAsync(
        string commandName,
        IReadOnlyList<string> arguments,
        int firstArgumentIndex,
        CancellationToken cancellationToken)
    {
        if (arguments.Count <= firstArgumentIndex + 1)
        {
            return SlashCommandResult.Failed(
                commandName,
                "Usage: /github run <owner/repo> <runId>");
        }

        if (!long.TryParse(arguments[firstArgumentIndex + 1], out var runId) || runId <= 0)
        {
            return SlashCommandResult.Failed(
                commandName,
                "Usage: /github run <owner/repo> <runId>");
        }

        if (_githubAppClient is null)
        {
            return SlashCommandResult.Succeeded(commandName, FormatGitHubAppUnavailable());
        }

        var repositoryFullName = arguments[firstArgumentIndex];
        var result = await _githubAppClient.ListWorkflowRunJobsAsync(
            repositoryFullName,
            runId,
            cancellationToken);
        if (!result.Success)
        {
            return SlashCommandResult.Succeeded(
                commandName,
                FormatGitHubAppSetup(result.Message, result.MissingSettings));
        }

        var builder = new StringBuilder()
            .AppendLine("Kam GitHub App workflow run jobs:")
            .AppendLine($"  run: {NormalizeMessage(result.RepositoryFullName)}#{result.RunId}")
            .AppendLine($"  status: {NormalizeMessage(result.Message)}");

        foreach (var job in result.Jobs
            .OrderBy(job => job.StartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(job => job.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(job => job.Id)
            .Take(50))
        {
            builder
                .AppendLine(
                    $"  - {NormalizeMessage(job.Name)} ({FormatWorkflowJobState(job)})")
                .AppendLine($"    {NormalizeMessage(job.HtmlUrl)}");
        }

        if (result.Jobs.Count == 0)
        {
            builder.AppendLine("  no jobs found for this workflow run");
        }
        else if (result.Jobs.Count > 50)
        {
            builder.AppendLine($"  ... {result.Jobs.Count - 50} more jobs hidden");
        }

        return SlashCommandResult.Succeeded(commandName, builder.ToString().TrimEnd());
    }

    private async Task<SlashCommandResult> RunGitHubAppPullRequestsAsync(
        string commandName,
        CancellationToken cancellationToken)
    {
        if (_githubAppClient is null)
        {
            return SlashCommandResult.Succeeded(commandName, FormatGitHubAppUnavailable());
        }

        var result = await _githubAppClient.ListPullRequestsAsync(cancellationToken);
        if (!result.Success)
        {
            return SlashCommandResult.Succeeded(
                commandName,
                FormatGitHubAppSetup(result.Message, result.MissingSettings));
        }

        var builder = new StringBuilder()
            .AppendLine("Kam GitHub App pull requests:")
            .AppendLine($"  status: {NormalizeMessage(result.Message)}");

        var activePullRequest = SelectPullRequestForContext(result.PullRequests);
        if (activePullRequest is not null)
        {
            SetActiveGitHubPullRequest(activePullRequest);
            builder.AppendLine(
                $"  active pull request: {NormalizeMessage(activePullRequest.RepositoryFullName)}#{activePullRequest.Number}");
        }

        foreach (var pullRequest in result.PullRequests
            .OrderByDescending(pullRequest => pullRequest.UpdatedAt ?? pullRequest.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(pullRequest => pullRequest.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pullRequest => pullRequest.Number)
            .Take(50))
        {
            var draftSuffix = pullRequest.IsDraft ? ", draft" : string.Empty;
            builder
                .AppendLine(
                    $"  - {NormalizeMessage(pullRequest.RepositoryFullName)}#{pullRequest.Number} {NormalizeMessage(pullRequest.Title)} ({NormalizeMessage(pullRequest.State)}, {NormalizeMessage(pullRequest.AuthorLogin)}, {NormalizeMessage(pullRequest.HeadRefName)} -> {NormalizeMessage(pullRequest.BaseRefName)}{draftSuffix})")
                .AppendLine($"    {NormalizeMessage(pullRequest.HtmlUrl)}");
        }

        if (result.PullRequests.Count == 0)
        {
            builder.AppendLine("  no open pull requests found");
        }
        else if (result.PullRequests.Count > 50)
        {
            builder.AppendLine($"  ... {result.PullRequests.Count - 50} more pull requests hidden");
        }

        return SlashCommandResult.Succeeded(commandName, builder.ToString().TrimEnd());
    }

    private static string FormatGitHubAppStatus(GitHubAppConnectionStatus status)
    {
        if (!status.IsConfigured)
        {
            return FormatGitHubAppSetup(status.Message, status.MissingSettings);
        }

        var builder = new StringBuilder()
            .AppendLine("Kam GitHub App:")
            .AppendLine($"  status: {(status.IsConnected ? "connected" : "configured but unavailable")}")
            .AppendLine($"  appId: {FormatConfiguredValue(status.AppId)}")
            .AppendLine($"  installationId: {FormatConfiguredValue(status.InstallationId)}")
            .AppendLine($"  endpoint: {FormatConfiguredValue(status.ApiBaseUrl)}")
            .AppendLine("  privateKey: (configured)");

        if (!string.IsNullOrWhiteSpace(status.AppName))
        {
            builder.AppendLine($"  app: {NormalizeMessage(status.AppName)}");
        }

        if (!string.IsNullOrWhiteSpace(status.AppSlug))
        {
            builder.AppendLine($"  slug: {NormalizeMessage(status.AppSlug)}");
        }

        if (status.RepositoryCount is not null)
        {
            builder.AppendLine($"  repositories: {status.RepositoryCount} accessible");
        }

        AppendGitHubAppPermissions(builder);

        if (!status.IsConnected)
        {
            builder.AppendLine($"  message: {NormalizeMessage(status.Message)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatGitHubAppUnavailable()
    {
        return FormatGitHubAppSetup(
            "GitHub App service is not registered.",
            ["GitHubApp:AppId", "GitHubApp:InstallationId", "GitHubApp:PrivateKeyPath"]);
    }

    private static string FormatGitHubAppSetup(
        string message,
        IReadOnlyList<string>? missingSettings)
    {
        var builder = new StringBuilder()
            .AppendLine("Kam GitHub App:")
            .AppendLine("  status: not configured")
            .AppendLine($"  message: {NormalizeMessage(message)}");

        if (missingSettings?.Count > 0)
        {
            builder.AppendLine($"  missing: {string.Join(", ", missingSettings)}");
        }

        AppendGitHubAppPermissions(builder);

        builder
            .AppendLine("  setup:")
            .AppendLine("    dotnet user-secrets set \"GitHubApp:AppId\" \"<app-id>\"")
            .AppendLine("    dotnet user-secrets set \"GitHubApp:InstallationId\" \"<installation-id>\"")
            .AppendLine("    dotnet user-secrets set \"GitHubApp:PrivateKeyPath\" \"<absolute-pem-path>\"")
            .AppendLine("  list repos: /github-app repos or /github repos")
            .AppendLine("  list PRs: /github-app prs or /github prs")
            .AppendLine("  list workflow runs: /github-app actions or /github actions")
            .AppendLine("  diagnose workflow run: /github-app diagnose <owner/repo> [runId] or /github diagnose <owner/repo> [runId]")
            .AppendLine("  workflow job logs: /github-app logs <owner/repo> <jobId> or /github logs <owner/repo> <jobId>")
            .AppendLine("  list workflow run jobs: /github-app run <owner/repo> <runId> or /github run <owner/repo> <runId>")
            .AppendLine("  CLI status: kam coding-agent /github app")
            .AppendLine("  CLI repos: kam coding-agent /github repos")
            .AppendLine("  CLI workflow runs: kam coding-agent /github actions")
            .AppendLine("  CLI diagnose workflow run: kam coding-agent /github diagnose <owner/repo> [runId]")
            .AppendLine("  CLI workflow job logs: kam coding-agent /github logs <owner/repo> <jobId>")
            .AppendLine("  CLI workflow run jobs: kam coding-agent /github run <owner/repo> <runId>")
            .AppendLine("  private key contents and installation tokens are never printed.");

        return builder.ToString().TrimEnd();
    }

    private async Task<SlashCommandResult> RunSkillTestAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (_skillTestService is null)
        {
            return SlashCommandResult.Failed("/test", "Skill test service is unavailable.");
        }

        if (arguments.Count == 0)
        {
            return SlashCommandResult.Failed(
                "/test",
                "Usage: /test <skillId>" + FormatSmokeCaseHint());
        }

        var skillId = arguments[0];
        var result = await _skillTestService.TestAsync(skillId, cancellationToken);
        var message = result.Success
            ? result.Message
            : string.IsNullOrWhiteSpace(result.ErrorMessage) ? result.Message : result.ErrorMessage;

        return result.Success
            ? SlashCommandResult.Succeeded("/test", $"Skill test passed for {skillId}: {message}")
            : SlashCommandResult.Failed("/test", $"Skill test failed for {skillId}: {message}");
    }

    private async Task<string> FormatReviewAsync(CancellationToken cancellationToken)
    {
        if (_skillHealthService is null)
        {
            return "Kam review: skill health service unavailable.";
        }

        var reports = await _skillHealthService.GetHealthAsync(cancellationToken);
        var attention = reports
            .Where(report => report.Status != SkillHealthStatus.Healthy)
            .OrderBy(report => report.SkillId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (attention.Length == 0)
        {
            return $"Kam review: {reports.Count} skills checked, no skill-health issues found.";
        }

        var builder = new StringBuilder()
            .AppendLine($"Kam review: {attention.Length} skills need attention.");
        foreach (var report in attention.Take(12))
        {
            builder.AppendLine($"  {report.SkillId}: {report.Status} - {report.Details}");
        }

        return builder.ToString().TrimEnd();
    }

    private string FormatCommandList(string? filter)
    {
        var commands = string.IsNullOrWhiteSpace(filter)
            ? Definitions
            : GetSuggestions("/" + filter).ToArray();

        var builder = new StringBuilder()
            .AppendLine("Kam slash commands:");

        foreach (var command in commands.OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"  {command.Usage} - {command.Summary}");
        }

        return builder.ToString().TrimEnd();
    }

    private string FormatSmokeCaseHint()
    {
        var cases = _evalCaseCatalog?.CreateSmokeCases()
            .Select(testCase => testCase.Plan.SkillId)
            .Where(skillId => !string.IsNullOrWhiteSpace(skillId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(skillId => skillId, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray() ?? [];

        return cases.Length == 0
            ? string.Empty
            : $"{Environment.NewLine}Available smoke tests: {string.Join(", ", cases)}";
    }

    private static void AppendGitHubAppPermissions(StringBuilder builder)
    {
        builder
            .AppendLine("  recommended permissions:")
            .AppendLine("    Metadata: read")
            .AppendLine("    Contents: read")
            .AppendLine("    Pull requests: read")
            .AppendLine("    Issues: read")
            .AppendLine("    Actions: read")
            .AppendLine("    Checks: read")
            .AppendLine("    Commit statuses: read")
            .AppendLine("    Dependabot alerts: read");
    }

    private static bool IsCommand(string value, params string[] names)
    {
        return names.Any(name => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "(empty)";
        }

        var normalized = SecretRedactor.Redact(message)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        const int maxLength = 500;
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private static string FormatWorkflowRunState(GitHubWorkflowRunSummary workflowRun)
    {
        var status = NormalizeMessage(workflowRun.Status);
        var conclusion = NormalizeMessage(workflowRun.Conclusion);
        return conclusion == "(empty)" ? status : $"{status}/{conclusion}";
    }

    private static string FormatWorkflowJobState(GitHubWorkflowJobSummary job)
    {
        var status = NormalizeMessage(job.Status);
        var conclusion = NormalizeMessage(job.Conclusion);
        return conclusion == "(empty)" ? status : $"{status}/{conclusion}";
    }

    private static string FormatWorkflowStepState(GitHubWorkflowJobStepSummary step)
    {
        var status = NormalizeMessage(step.Status);
        var conclusion = NormalizeMessage(step.Conclusion);
        return conclusion == "(empty)" ? status : $"{status}/{conclusion}";
    }

    private static string FormatWorkflowDiagnosis(
        GitHubWorkflowJobListResult jobs,
        GitHubWorkflowRunSummary? selectedRun,
        string? selectionMessage)
    {
        var unhealthyJobs = jobs.Jobs
            .Where(IsUnhealthyWorkflowJob)
            .OrderBy(job => job.StartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(job => job.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(job => job.Id)
            .ToArray();
        var unhealthyStepsByJob = unhealthyJobs.ToDictionary(
            job => job.Id,
            job => job.Steps
                .Where(IsUnhealthyWorkflowStep)
                .OrderBy(step => step.Number)
                .ThenBy(step => step.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var unhealthyStepCount = unhealthyStepsByJob.Values.Sum(steps => steps.Length);

        var builder = new StringBuilder()
            .AppendLine("Kam GitHub CI doctor:")
            .AppendLine($"  run: {NormalizeMessage(jobs.RepositoryFullName)}#{jobs.RunId}");

        if (!string.IsNullOrWhiteSpace(selectionMessage))
        {
            builder.AppendLine($"  selected: {NormalizeMessage(selectionMessage)}");
        }

        if (selectedRun is not null)
        {
            builder
                .AppendLine($"  workflow: {NormalizeMessage(selectedRun.Name)} ({FormatWorkflowRunState(selectedRun)}, {NormalizeMessage(selectedRun.Event)}, {NormalizeMessage(selectedRun.HeadBranch)})")
                .AppendLine($"  title: {NormalizeMessage(selectedRun.DisplayTitle)}");
        }

        builder.AppendLine(
            $"  diagnosis: {unhealthyJobs.Length} {Pluralize(unhealthyJobs.Length, "failing job", "failing jobs")}, {unhealthyStepCount} {Pluralize(unhealthyStepCount, "failing step", "failing steps")}.");

        if (unhealthyJobs.Length == 0)
        {
            builder.AppendLine("  no failing or active jobs found for this workflow run");
            return builder.ToString().TrimEnd();
        }

        foreach (var job in unhealthyJobs.Take(10))
        {
            builder
                .AppendLine($"  - {NormalizeMessage(job.Name)} ({FormatWorkflowJobState(job)})")
                .AppendLine($"    {NormalizeMessage(job.HtmlUrl)}");

            var steps = unhealthyStepsByJob[job.Id];
            if (steps.Length == 0)
            {
                builder.AppendLine("    step: no failed step details returned");
            }
            else
            {
                foreach (var step in steps.Take(8))
                {
                    builder.AppendLine(
                        $"    step: {NormalizeMessage(step.Name)} ({FormatWorkflowStepState(step)})");
                }

                if (steps.Length > 8)
                {
                    builder.AppendLine($"    ... {steps.Length - 8} more failing steps hidden");
                }
            }

            builder.AppendLine($"    next: inspect job log for {NormalizeMessage(job.Name)}");
        }

        if (unhealthyJobs.Length > 10)
        {
            builder.AppendLine($"  ... {unhealthyJobs.Length - 10} more failing jobs hidden");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatWorkflowJobLogs(GitHubWorkflowJobLogResult result)
    {
        var builder = new StringBuilder()
            .AppendLine("Kam GitHub App workflow job logs:")
            .AppendLine($"  job: {NormalizeMessage(result.RepositoryFullName)}#{result.JobId}")
            .AppendLine($"  status: {NormalizeMessage(result.Message)}");

        if (!string.IsNullOrWhiteSpace(result.DownloadUrl))
        {
            builder
                .AppendLine($"  download: {NormalizeMessage(result.DownloadUrl)}")
                .AppendLine("  expires: GitHub log download URLs expire after 1 minute.");
        }

        if (!string.IsNullOrWhiteSpace(result.LogPreview))
        {
            builder.AppendLine("  preview:");
            foreach (var line in result.LogPreview
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(20))
            {
                builder.AppendLine($"    {NormalizeMessage(line)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static GitHubWorkflowJobSummary? SelectWorkflowJobForAutoFix(
        IReadOnlyList<GitHubWorkflowJobSummary> jobs)
    {
        return jobs
            .Where(IsUnhealthyWorkflowJob)
            .OrderBy(job => job.StartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(job => job.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(job => job.Id)
            .FirstOrDefault();
    }

    private static GitHubWorkflowRunSummary? SelectWorkflowRunForContext(
        IReadOnlyList<GitHubWorkflowRunSummary> workflowRuns)
    {
        var orderedRuns = workflowRuns
            .OrderByDescending(run => run.UpdatedAt ?? run.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(run => run.Id)
            .ToArray();

        return orderedRuns.FirstOrDefault(IsUnhealthyWorkflowRun)
            ?? orderedRuns.FirstOrDefault();
    }

    private static GitHubPullRequestSummary? SelectPullRequestForContext(
        IReadOnlyList<GitHubPullRequestSummary> pullRequests)
    {
        return pullRequests
            .OrderByDescending(pullRequest => pullRequest.UpdatedAt ?? pullRequest.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(pullRequest => pullRequest.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pullRequest => pullRequest.Number)
            .FirstOrDefault();
    }

    private void SetActiveGitHubWorkflowRun(GitHubWorkflowRunSummary workflowRun)
    {
        _activeGitHubWorkflowRun = workflowRun;
        _activeGitHubRepositoryFullName = workflowRun.RepositoryFullName;
        SaveApplicationSessionContext();
    }

    private void SetActiveGitHubPullRequest(GitHubPullRequestSummary pullRequest)
    {
        _activeGitHubPullRequest = pullRequest;
        _activeGitHubRepositoryFullName = pullRequest.RepositoryFullName;
        SaveApplicationSessionContext();
    }

    private void SaveApplicationSessionContext()
    {
        if (_applicationSessionContextStore is null)
        {
            return;
        }

        var context = _applicationSessionContextStore.Load();
        context.GitHub.RepositoryFullName = _activeGitHubRepositoryFullName ?? string.Empty;
        context.GitHub.ActiveWorkflowRun = _activeGitHubWorkflowRun is null
            ? context.GitHub.ActiveWorkflowRun
            : MapWorkflowRun(_activeGitHubWorkflowRun);
        context.GitHub.ActivePullRequest = _activeGitHubPullRequest is null
            ? context.GitHub.ActivePullRequest
            : MapPullRequest(_activeGitHubPullRequest);
        _applicationSessionContextStore.Save(context);
    }

    private static GitHubWorkflowRunSummary MapWorkflowRun(GitHubWorkflowRunSessionContext workflowRun)
    {
        return new GitHubWorkflowRunSummary(
            workflowRun.RepositoryFullName,
            workflowRun.RunId,
            workflowRun.Name,
            workflowRun.DisplayTitle,
            workflowRun.Status,
            workflowRun.Conclusion,
            workflowRun.Event,
            workflowRun.HeadBranch,
            workflowRun.HtmlUrl,
            workflowRun.CreatedAt,
            workflowRun.UpdatedAt);
    }

    private static GitHubWorkflowRunSessionContext MapWorkflowRun(GitHubWorkflowRunSummary workflowRun)
    {
        return new GitHubWorkflowRunSessionContext
        {
            RepositoryFullName = workflowRun.RepositoryFullName,
            RunId = workflowRun.Id,
            Name = workflowRun.Name,
            DisplayTitle = workflowRun.DisplayTitle,
            Status = workflowRun.Status,
            Conclusion = workflowRun.Conclusion,
            Event = workflowRun.Event,
            HeadBranch = workflowRun.HeadBranch,
            HtmlUrl = workflowRun.HtmlUrl,
            CreatedAt = workflowRun.CreatedAt,
            UpdatedAt = workflowRun.UpdatedAt
        };
    }

    private static GitHubPullRequestSummary MapPullRequest(GitHubPullRequestSessionContext pullRequest)
    {
        return new GitHubPullRequestSummary(
            pullRequest.RepositoryFullName,
            pullRequest.Number,
            pullRequest.Title,
            pullRequest.State,
            pullRequest.AuthorLogin,
            pullRequest.HtmlUrl,
            pullRequest.HeadRefName,
            pullRequest.BaseRefName,
            pullRequest.IsDraft,
            pullRequest.CreatedAt,
            pullRequest.UpdatedAt);
    }

    private static GitHubPullRequestSessionContext MapPullRequest(GitHubPullRequestSummary pullRequest)
    {
        return new GitHubPullRequestSessionContext
        {
            RepositoryFullName = pullRequest.RepositoryFullName,
            Number = pullRequest.Number,
            Title = pullRequest.Title,
            State = pullRequest.State,
            AuthorLogin = pullRequest.AuthorLogin,
            HtmlUrl = pullRequest.HtmlUrl,
            HeadRefName = pullRequest.HeadRefName,
            BaseRefName = pullRequest.BaseRefName,
            IsDraft = pullRequest.IsDraft,
            CreatedAt = pullRequest.CreatedAt,
            UpdatedAt = pullRequest.UpdatedAt
        };
    }

    private static string BuildGitHubAutoFixRuntimeTask(
        GitHubWorkflowJobListResult jobs,
        GitHubWorkflowRunSummary? selectedRun,
        string? selectionMessage,
        GitHubWorkflowJobSummary selectedJob,
        GitHubWorkflowJobLogResult log)
    {
        var builder = new StringBuilder()
            .AppendLine("GitHub CI auto-fix request.")
            .AppendLine($"Repository/run: {NormalizeMessage(jobs.RepositoryFullName)}#{jobs.RunId}");

        if (!string.IsNullOrWhiteSpace(selectionMessage))
        {
            builder.AppendLine($"Selection: {NormalizeMessage(selectionMessage)}");
        }

        if (selectedRun is not null)
        {
            builder
                .AppendLine($"Workflow: {NormalizeMessage(selectedRun.Name)} ({FormatWorkflowRunState(selectedRun)})")
                .AppendLine($"Title: {NormalizeMessage(selectedRun.DisplayTitle)}")
                .AppendLine($"Branch: {NormalizeMessage(selectedRun.HeadBranch)}")
                .AppendLine($"Event: {NormalizeMessage(selectedRun.Event)}");
        }

        builder
            .AppendLine($"Failing job: {NormalizeMessage(selectedJob.Name)} ({FormatWorkflowJobState(selectedJob)})")
            .AppendLine($"Job URL: {NormalizeMessage(selectedJob.HtmlUrl)}");

        var failingSteps = selectedJob.Steps
            .Where(IsUnhealthyWorkflowStep)
            .OrderBy(step => step.Number)
            .ThenBy(step => step.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (failingSteps.Length > 0)
        {
            builder.AppendLine("Failing steps:");
            foreach (var step in failingSteps.Take(8))
            {
                builder.AppendLine($"- {NormalizeMessage(step.Name)} ({FormatWorkflowStepState(step)})");
            }
        }

        if (!string.IsNullOrWhiteSpace(log.LogPreview))
        {
            builder
                .AppendLine("Job log preview:")
                .AppendLine(TrimForRuntimePrompt(log.LogPreview, 4000));
        }
        else if (!string.IsNullOrWhiteSpace(log.DownloadUrl))
        {
            builder.AppendLine($"Job log download URL: {NormalizeMessage(log.DownloadUrl)}");
        }

        builder
            .AppendLine("Task:")
            .AppendLine("Diagnose the likely source change needed in this workspace. If a file change or test run is needed, propose approval-gated actions only; do not claim changes or tests were executed.");

        return builder.ToString().TrimEnd();
    }

    private static GitHubWorkflowRunSummary? SelectWorkflowRunForDiagnosis(
        IReadOnlyList<GitHubWorkflowRunSummary> workflowRuns,
        string repositoryFullName)
    {
        var repositoryRuns = workflowRuns
            .Where(run => run.RepositoryFullName.Equals(repositoryFullName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.UpdatedAt ?? run.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(run => run.Id)
            .ToArray();

        return repositoryRuns.FirstOrDefault(IsUnhealthyWorkflowRun)
            ?? repositoryRuns.FirstOrDefault();
    }

    private static bool IsUnhealthyWorkflowRun(GitHubWorkflowRunSummary workflowRun)
    {
        return !workflowRun.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || IsFailureConclusion(workflowRun.Conclusion);
    }

    private static bool IsUnhealthyWorkflowJob(GitHubWorkflowJobSummary job)
    {
        return !job.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || IsFailureConclusion(job.Conclusion);
    }

    private static bool IsUnhealthyWorkflowStep(GitHubWorkflowJobStepSummary step)
    {
        return !step.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || IsFailureConclusion(step.Conclusion);
    }

    private static bool IsFailureConclusion(string? conclusion)
    {
        return !string.IsNullOrWhiteSpace(conclusion)
            && !conclusion.Equals("success", StringComparison.OrdinalIgnoreCase)
            && !conclusion.Equals("neutral", StringComparison.OrdinalIgnoreCase)
            && !conclusion.Equals("skipped", StringComparison.OrdinalIgnoreCase);
    }

    private static string Pluralize(int count, string singular, string plural)
    {
        return count == 1 ? singular : plural;
    }

    private static string TrimForRuntimePrompt(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = SecretRedactor.Redact(value)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private static string NormalizeAlias(string commandName)
    {
        return commandName switch
        {
            "/commands" => "/help",
            "/home" => "/coordinator",
            "/runtime" => "/diagnostics",
            "/models" => "/model",
            "/quota" or "/balance" or "/rate-limit" => "/limits",
            "/task-agent" => "/agent",
            _ => commandName
        };
    }

    private static string GetCommandToken(string input)
    {
        return input.TrimStart()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
    }

    private static IReadOnlyList<string> GetArguments(string input)
    {
        return input.TrimStart()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .ToArray();
    }

    private static string GetArgumentText(string input)
    {
        var trimmed = input.TrimStart();
        var commandToken = GetCommandToken(input);
        if (string.IsNullOrWhiteSpace(commandToken) || trimmed.Length <= commandToken.Length)
        {
            return string.Empty;
        }

        return trimmed[commandToken.Length..].Trim();
    }

    private static string GetArgumentTextAfterFirstToken(string argumentText)
    {
        var trimmed = argumentText.TrimStart();
        var separatorIndex = trimmed.IndexOf(' ');
        return separatorIndex < 0
            ? string.Empty
            : trimmed[(separatorIndex + 1)..].Trim();
    }

    private static string? NormalizePathArgument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private static string FormatConfiguredValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(not configured)" : NormalizeMessage(value);
    }

    private static string FormatSecretStatus(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(not configured)" : "(configured)";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kib = bytes / 1024d;
        if (kib < 1024)
        {
            return $"{kib:F1} KiB";
        }

        var mib = kib / 1024d;
        return $"{mib:F1} MiB";
    }
}
