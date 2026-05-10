using System.Text;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.CodingAgent;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Core.Models.SlashCommands;
using SmartVoiceAgent.Infrastructure.Mcp;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class SlashCommandService : ISlashCommandService
{
    private static readonly SlashCommandDefinition[] Definitions =
    [
        new("/help", "Show available slash commands.", "/help", "General", ["/commands"]),
        new("/commands", "List slash commands, optionally filtered.", "/commands [filter]", "General", ["/help"]),
        new("/version", "Show current Kam version and update channel.", "/version", "Updates"),
        new("/update", "Check for a newer Kam release.", "/update [check|download|restart]", "Updates"),
        new("/download", "Download the latest Kam release package.", "/download", "Updates"),
        new("/restart", "Show the Kam restart handoff plan.", "/restart [packagePath]", "Updates"),
        new("/status", "Show runtime and skill health status.", "/status", "Runtime"),
        new("/permissions", "Show chat command permission boundaries.", "/permissions", "Runtime"),
        new("/diff", "Show how to inspect the workspace diff from coding-agent mode.", "/diff", "Workflow"),
        new("/dependabot", "Show how to run dependency audit and Dependabot checks.", "/dependabot", "Workflow"),
        new("/github", "Show how to inspect GitHub PR and workflow status.", "/github", "Workflow"),
        new("/github-app", "Show GitHub App setup and repository permission guidance.", "/github-app", "Workflow"),
        new("/plugins", "Show skill/plugin health summary.", "/plugins", "Skills"),
        new("/mcp", "Show configured MCP endpoint status.", "/mcp", "Integrations"),
        new("/agents", "Show registered runtime agents.", "/agents", "Runtime"),
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
    private readonly IApplicationRestartPlanner? _applicationRestartPlanner;

    public SlashCommandService(
        IVoiceAgentHostControl? hostControl = null,
        ISkillHealthService? skillHealthService = null,
        ISkillTestService? skillTestService = null,
        ISkillEvalCaseCatalog? evalCaseCatalog = null,
        IAgentRegistry? agentRegistry = null,
        IOptions<CodingAgentOptions>? codingAgentOptions = null,
        IOptions<McpOptions>? mcpOptions = null,
        IApplicationUpdateService? applicationUpdateService = null,
        IApplicationRestartPlanner? applicationRestartPlanner = null)
    {
        _hostControl = hostControl;
        _skillHealthService = skillHealthService;
        _skillTestService = skillTestService;
        _evalCaseCatalog = evalCaseCatalog;
        _agentRegistry = agentRegistry;
        _codingAgentOptions = codingAgentOptions?.Value ?? new CodingAgentOptions();
        _mcpOptions = mcpOptions?.Value ?? new McpOptions();
        _applicationUpdateService = applicationUpdateService;
        _applicationRestartPlanner = applicationRestartPlanner;
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
            "/status" => SlashCommandResult.Succeeded("/status", await FormatStatusAsync(cancellationToken)),
            "/permissions" => SlashCommandResult.Succeeded("/permissions", FormatPermissions()),
            "/diff" => SlashCommandResult.Succeeded("/diff", FormatCodingAgentWorkflow("/diff")),
            "/dependabot" => SlashCommandResult.Succeeded("/dependabot", FormatCodingAgentWorkflow("/dependabot")),
            "/github" => SlashCommandResult.Succeeded("/github", FormatCodingAgentWorkflow("/github")),
            "/github-app" => SlashCommandResult.Succeeded("/github-app", FormatGitHubAppGuidance()),
            "/plugins" => SlashCommandResult.Succeeded("/plugins", await FormatPluginsAsync(cancellationToken)),
            "/mcp" => SlashCommandResult.Succeeded("/mcp", FormatMcp()),
            "/agents" => SlashCommandResult.Succeeded("/agents", FormatAgents()),
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

    private string FormatVersion()
    {
        return string.Join(Environment.NewLine, [
            "Kam version:",
            $"  current: {_applicationUpdateService?.CurrentVersion ?? "(unavailable)"}",
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
            return SlashCommandResult.Failed("/download", download.Message);
        }

        var nextStep = download.IsVerified
            ? "next: /restart <file>"
            : "next: verify release package before restart";

        return SlashCommandResult.Succeeded(
            "/download",
            string.Join(Environment.NewLine, [
                "Kam update downloaded:",
                $"  version: {download.Version ?? "(unknown)"}",
                $"  file: {download.FilePath}",
                $"  size: {FormatBytes(download.SizeBytes ?? 0)}",
                $"  verification: {download.VerificationStatus}",
                $"  {nextStep}"
            ]));
    }

    private string FormatRestartPlan(string? updatePackagePath)
    {
        if (_applicationRestartPlanner is null)
        {
            return "Kam restart planner is unavailable.";
        }

        var plan = _applicationRestartPlanner.CreateRestartPlan(NormalizePathArgument(updatePackagePath));
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

    private string FormatAgents()
    {
        var names = _agentRegistry?.GetAllAgentNames()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (names.Length == 0)
        {
            return "Kam agents: no runtime agents are registered yet.";
        }

        var builder = new StringBuilder()
            .AppendLine("Kam agents:");
        foreach (var name in names)
        {
            builder.AppendLine($"  {name}");
        }

        return builder.ToString().TrimEnd();
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

    private static string FormatGitHubAppGuidance()
    {
        return string.Join(Environment.NewLine, [
            "Kam GitHub App:",
            "  purpose: read repository metadata for coding-agent workflows without a broad PAT",
            "  recommended permissions:",
            "    Metadata: read",
            "    Contents: read",
            "    Pull requests: read",
            "    Issues: read",
            "    Actions: read",
            "    Checks: read",
            "    Commit statuses: read",
            "    Dependabot alerts: read",
            "  setup:",
            "    dotnet user-secrets set \"GitHubApp:AppId\" \"<app-id>\"",
            "    dotnet user-secrets set \"GitHubApp:InstallationId\" \"<installation-id>\"",
            "    dotnet user-secrets set \"GitHubApp:PrivateKeyPath\" \"<absolute-pem-path>\"",
            "  run from CLI: kam coding-agent /github app",
            "  list repos: kam coding-agent /github repos",
            "  chat slash commands do not print private keys or installation tokens"
        ]);
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

    private static string NormalizeAlias(string commandName)
    {
        return commandName switch
        {
            "/commands" => "/help",
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
        return string.IsNullOrWhiteSpace(value) ? "(not configured)" : value.Trim();
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
