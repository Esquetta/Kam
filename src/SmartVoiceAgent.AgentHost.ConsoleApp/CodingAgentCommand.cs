using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.CodingAgent;
using SmartVoiceAgent.Core.Models.Commands;
using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Core.Security;
using SmartVoiceAgent.Infrastructure.Mcp;

namespace SmartVoiceAgent.AgentHost.ConsoleApp;

public sealed class CodingAgentCommandOptions
{
    public string CommandText { get; init; } = CodingAgentCommand.DefaultCommandText;

    public string WorkspaceRoot { get; init; } = Environment.CurrentDirectory;

    public string ApprovalMode { get; init; } = "workspace-write";

    public string? SummaryPath { get; init; }

    public bool HooksEnabled { get; init; }

    public IReadOnlyList<string> PreTestHooks { get; init; } = [];

    public IReadOnlyList<string> PostTestHooks { get; init; } = [];

    public IReadOnlyList<string> PreReviewHooks { get; init; } = [];

    public IReadOnlyList<string> PostReviewHooks { get; init; } = [];
}

public sealed class CodingAgentCommand
{
    public const string SwitchName = "--coding-agent";
    public const string DefaultCommandText = "/status";

    private static readonly string[] TestArguments = [
        "test",
        "tests/SmartVoiceAgent.Tests/SmartVoiceAgent.Tests.csproj",
        "--configuration",
        "Release",
        "--verbosity",
        "normal"
    ];

    private static readonly string[] DependencyAuditArguments = [
        "list",
        "Kam.sln",
        "package",
        "--vulnerable",
        "--include-transitive"
    ];

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(900);
    private static readonly TimeSpan ReviewStepTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StatusStepTimeout = TimeSpan.FromSeconds(30);
    private const string WorktreeExecuteFlag = "--execute";

    private readonly ICommandRuntimeService _runtime;
    private readonly ICodingAgentProcessRunner _processRunner;
    private readonly ISkillHealthService? _skillHealthService;
    private readonly ISkillTestService? _skillTestService;
    private readonly IAgentRegistry? _agentRegistry;
    private readonly IGitHubAppClient? _githubAppClient;
    private readonly McpOptions _mcpOptions;

    public CodingAgentCommand(
        ICommandRuntimeService runtime,
        ICodingAgentProcessRunner? processRunner = null,
        ISkillHealthService? skillHealthService = null,
        ISkillTestService? skillTestService = null,
        IAgentRegistry? agentRegistry = null,
        IGitHubAppClient? githubAppClient = null,
        IOptions<McpOptions>? mcpOptions = null)
    {
        _runtime = runtime;
        _processRunner = processRunner ?? new CodingAgentProcessRunner();
        _skillHealthService = skillHealthService;
        _skillTestService = skillTestService;
        _agentRegistry = agentRegistry;
        _githubAppClient = githubAppClient;
        _mcpOptions = mcpOptions?.Value ?? new McpOptions();
    }

    public static bool IsRequested(IReadOnlyList<string> args)
    {
        return args.Any(arg => arg.Equals(SwitchName, StringComparison.OrdinalIgnoreCase))
            || args.FirstOrDefault()?.StartsWith("/", StringComparison.Ordinal) == true;
    }

    public static CodingAgentCommandOptions ParseOptions(IReadOnlyList<string> args)
    {
        var commandText = string.Empty;
        var workspaceRoot = Environment.CurrentDirectory;
        var approvalMode = "workspace-write";
        var summaryPath = string.Empty;
        var hooksEnabled = false;
        var preTestHooks = new List<string>();
        var postTestHooks = new List<string>();
        var preReviewHooks = new List<string>();
        var postReviewHooks = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg.Equals(SwitchName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsValueSwitch(arg, "--workspace", "--cwd") && index + 1 < args.Count)
            {
                workspaceRoot = args[++index];
                continue;
            }

            if (IsValueSwitch(arg, "--approval-mode", "--permissions") && index + 1 < args.Count)
            {
                approvalMode = args[++index];
                continue;
            }

            if (IsValueSwitch(arg, "--command", "--coding-agent-command") && index + 1 < args.Count)
            {
                commandText = args[++index];
                continue;
            }

            if (IsValueSwitch(arg, "--summary", "--coding-agent-summary") && index + 1 < args.Count)
            {
                summaryPath = args[++index];
                continue;
            }

            if (arg.Equals("--show-hooks", StringComparison.OrdinalIgnoreCase))
            {
                hooksEnabled = true;
                continue;
            }

            if (IsValueSwitch(arg, "--pre-test-hook") && index + 1 < args.Count)
            {
                preTestHooks.Add(args[++index]);
                continue;
            }

            if (IsValueSwitch(arg, "--post-test-hook") && index + 1 < args.Count)
            {
                postTestHooks.Add(args[++index]);
                continue;
            }

            if (IsValueSwitch(arg, "--pre-review-hook") && index + 1 < args.Count)
            {
                preReviewHooks.Add(args[++index]);
                continue;
            }

            if (IsValueSwitch(arg, "--post-review-hook") && index + 1 < args.Count)
            {
                postReviewHooks.Add(args[++index]);
                continue;
            }

            if (arg.StartsWith("/", StringComparison.Ordinal))
            {
                commandText = string.Join(' ', args.Skip(index));
                break;
            }
        }

        return new CodingAgentCommandOptions
        {
            CommandText = string.IsNullOrWhiteSpace(commandText) ? DefaultCommandText : commandText.Trim(),
            WorkspaceRoot = NormalizeWorkspaceRoot(workspaceRoot),
            ApprovalMode = string.IsNullOrWhiteSpace(approvalMode) ? "workspace-write" : approvalMode.Trim(),
            SummaryPath = string.IsNullOrWhiteSpace(summaryPath) ? null : summaryPath,
            HooksEnabled = hooksEnabled,
            PreTestHooks = preTestHooks,
            PostTestHooks = postTestHooks,
            PreReviewHooks = preReviewHooks,
            PostReviewHooks = postReviewHooks
        };
    }

    public static IReadOnlyDictionary<string, string?> CreateConfigurationOverrides(
        CodingAgentCommandOptions options)
    {
        return new Dictionary<string, string?>
        {
            [$"{CodingAgentOptions.SectionName}:IsEnabled"] = "true",
            [$"{CodingAgentOptions.SectionName}:WorkspaceRoot"] = options.WorkspaceRoot,
            [$"{CodingAgentOptions.SectionName}:ApprovalMode"] = options.ApprovalMode,
            [$"{CodingAgentOptions.SectionName}:RequireShellAllowList"] = "true"
        };
    }

    public async Task<int> RunAsync(
        CodingAgentCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!Directory.Exists(options.WorkspaceRoot))
        {
            await error.WriteLineAsync($"Workspace does not exist: {options.WorkspaceRoot}");
            return 2;
        }

        var summaryPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(options.SummaryPath)
            && !TryResolveWorkspacePath(options.WorkspaceRoot, options.SummaryPath, out summaryPath))
        {
            await error.WriteLineAsync($"Summary path must stay inside the workspace: {options.SummaryPath}");
            return 2;
        }

        var result = options.CommandText.StartsWith("/", StringComparison.Ordinal)
            ? await RunSlashCommandAsync(options, output, cancellationToken)
            : await RunRuntimeCommandAsync(options, output, error, cancellationToken);

        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            await WriteSummaryAsync(options, result, summaryPath, cancellationToken);
        }

        return result.ExitCode;
    }

    private async Task<CodingAgentCommandResult> RunSlashCommandAsync(
        CodingAgentCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var commandName = options.CommandText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim()
            .ToLowerInvariant();

        var result = commandName switch
        {
            "/help" => CodingAgentCommandResult.Success(FormatHelp()),
            "/status" => CodingAgentCommandResult.Success(await FormatStatusAsync(options, cancellationToken)),
            "/permissions" => CodingAgentCommandResult.Success(FormatPermissions(options)),
            "/diff" => CodingAgentCommandResult.Success(await FormatDiffAsync(options, cancellationToken)),
            "/review" => await RunReviewWorkflowAsync(options, cancellationToken),
            "/test" => await RunTestWorkflowAsync(options, cancellationToken),
            "/dependabot" => await RunDependabotWorkflowAsync(options, cancellationToken),
            "/github" or "/github-app" => await RunGithubCommandAsync(options, cancellationToken),
            "/plugins" => await FormatPluginsAsync(cancellationToken),
            "/mcp" => CodingAgentCommandResult.Success(FormatMcp()),
            "/agents" => CodingAgentCommandResult.Success(FormatAgents()),
            "/worktree" => await RunWorktreeCommandAsync(options, cancellationToken),
            "/hooks" => CodingAgentCommandResult.Success(FormatHooks(options)),
            _ => new CodingAgentCommandResult(2, $"Unknown coding command: {options.CommandText}", false)
        };

        await output.WriteLineAsync(result.Message);
        return result;
    }

    private async Task<CodingAgentCommandResult> RunRuntimeCommandAsync(
        CodingAgentCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await _runtime.ExecuteAsync(options.CommandText, cancellationToken);
        var status = result.Success ? "PASS" : "FAIL";

        await output.WriteLineAsync(
            $"[{status}] coding-agent - {NormalizeMessage(options.CommandText)} -> {NormalizeMessage(result.SkillId)}");
        await output.WriteLineAsync(NormalizeMessage(result.Message));

        if (result.Success && !result.RequiresConfirmation)
        {
            return new CodingAgentCommandResult(0, result.Message, false);
        }

        var reason = result.RequiresConfirmation
            ? "Coding command requires confirmation before execution."
            : $"Coding command failed: {NormalizeMessage(result.Message)}";
        await error.WriteLineAsync(reason);

        return new CodingAgentCommandResult(1, result.Message, result.RequiresConfirmation);
    }

    private static string FormatHelp()
    {
        return string.Join(Environment.NewLine, [
            "Kam coding-agent commands:",
            "  /help          Show this help.",
            "  /status        Show workspace and git status.",
            "  /permissions   Show active workspace permission mode.",
            "  /diff          Show working tree diff summary.",
            "  /review        Run deterministic pre-commit review checks.",
            "  /test          Run the configured test command.",
            "  /dependabot    Run dependency audit and list Dependabot PRs when gh is available.",
            "  /github        Show GitHub PR and workflow status when gh is available.",
            "  /github app    Show GitHub App connection and required repository permissions.",
            "  /github repos  List repositories visible through the configured GitHub App.",
            "  /github actions  List GitHub Actions workflow runs visible through the configured GitHub App.",
            "  /github diagnose  Diagnose one GitHub Actions workflow run.",
            "  /github logs   Get a temporary download URL or preview for one GitHub Actions job log.",
            "  /github run    List jobs for one GitHub Actions workflow run.",
            "  /github-app    Alias for GitHub App repository, PR, workflow, and run commands.",
            "  /plugins       Show skill/plugin health summary.",
            "  /mcp           Show configured MCP endpoints.",
            "  /agents        Show registered runtime agents and coding role templates.",
            "  /worktree      Show worktree status or plan a new worktree.",
            "  /hooks         Show configured coding-agent hooks.",
            string.Empty,
            "Plain text input is sent to the skill-first command runtime."
        ]);
    }

    private static string FormatPermissions(CodingAgentCommandOptions options)
    {
        return string.Join(Environment.NewLine, [
            "Kam coding-agent permissions:",
            $"  workspace: {options.WorkspaceRoot}",
            $"  approvalMode: {options.ApprovalMode}",
            "  files: read/write scoped to workspace when coding mode is active",
            "  shell: working directory scoped to workspace",
            "  shellAllowList: required before shell.run can execute commands"
        ]);
    }

    private static async Task<string> FormatStatusAsync(
        CodingAgentCommandOptions options,
        CancellationToken cancellationToken)
    {
        var gitStatus = await RunGitAsync(options.WorkspaceRoot, "status --short --branch", cancellationToken);
        return string.Join(Environment.NewLine, [
            "Kam coding-agent status:",
            $"  workspace: {options.WorkspaceRoot}",
            $"  approvalMode: {options.ApprovalMode}",
            string.IsNullOrWhiteSpace(gitStatus) ? "  git: no status output" : gitStatus.TrimEnd()
        ]);
    }

    private static async Task<string> FormatDiffAsync(
        CodingAgentCommandOptions options,
        CancellationToken cancellationToken)
    {
        var unstaged = await RunGitAsync(options.WorkspaceRoot, "diff --stat", cancellationToken);
        var staged = await RunGitAsync(options.WorkspaceRoot, "diff --cached --stat", cancellationToken);
        var combined = string.Join(
            Environment.NewLine,
            new[] { unstaged, staged }.Where(item => !string.IsNullOrWhiteSpace(item))).Trim();

        return string.IsNullOrWhiteSpace(combined)
            ? "No working tree diff."
            : combined;
    }

    private async Task<CodingAgentCommandResult> RunTestWorkflowAsync(
        CodingAgentCommandOptions options,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .AppendLine("Kam coding-agent test:");

        var test = await RunProcessStepAsync(
            "dotnet test",
            "dotnet",
            TestArguments,
            options,
            cancellationToken,
            timeout: TestTimeout);
        AppendWorkflowSection(builder, test);

        return new CodingAgentCommandResult(test.ExitCode, builder.ToString().TrimEnd(), false);
    }

    private async Task<CodingAgentCommandResult> RunReviewWorkflowAsync(
        CodingAgentCommandOptions options,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .AppendLine("Kam coding-agent review:");

        var status = await RunProcessStepWithRawAsync(
            "git status",
            "git",
            ["status", "--short", "--branch"],
            options,
            cancellationToken,
            timeout: ReviewStepTimeout);
        AppendWorkflowSection(builder, status.Step);

        var unstagedWhitespace = await RunProcessStepWithRawAsync(
            "git diff --check",
            "git",
            ["diff", "--check"],
            options,
            cancellationToken,
            timeout: ReviewStepTimeout);
        AppendWorkflowSection(builder, unstagedWhitespace.Step);

        var stagedWhitespace = await RunProcessStepWithRawAsync(
            "git diff --cached --check",
            "git",
            ["diff", "--cached", "--check"],
            options,
            cancellationToken,
            timeout: ReviewStepTimeout);
        AppendWorkflowSection(builder, stagedWhitespace.Step);

        var unstagedStat = await RunProcessStepWithRawAsync(
            "git diff --stat",
            "git",
            ["diff", "--stat"],
            options,
            cancellationToken,
            timeout: ReviewStepTimeout);
        AppendWorkflowSection(builder, unstagedStat.Step);

        var stagedStat = await RunProcessStepWithRawAsync(
            "git diff --cached --stat",
            "git",
            ["diff", "--cached", "--stat"],
            options,
            cancellationToken,
            timeout: ReviewStepTimeout);
        AppendWorkflowSection(builder, stagedStat.Step);

        if (string.IsNullOrWhiteSpace(unstagedStat.Raw.CombinedOutput())
            && string.IsNullOrWhiteSpace(stagedStat.Raw.CombinedOutput()))
        {
            builder.AppendLine().AppendLine("No working tree diff to review.");
        }

        var exitCode = new[]
            {
                status.Step.ExitCode,
                unstagedWhitespace.Step.ExitCode,
                stagedWhitespace.Step.ExitCode,
                unstagedStat.Step.ExitCode,
                stagedStat.Step.ExitCode
            }
            .FirstOrDefault(code => code != 0);
        return new CodingAgentCommandResult(exitCode, builder.ToString().TrimEnd(), false);
    }

    private async Task<CodingAgentCommandResult> RunDependabotWorkflowAsync(
        CodingAgentCommandOptions options,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .AppendLine("Kam coding-agent Dependabot/dependency audit:");

        var audit = await RunProcessStepAsync(
            "dotnet vulnerable package audit",
            "dotnet",
            DependencyAuditArguments,
            options,
            cancellationToken,
            timeout: TestTimeout);
        AppendWorkflowSection(builder, audit);

        var dependabotPrs = await RunProcessStepAsync(
            "open Dependabot PRs",
            "gh",
            ["pr", "list", "--state", "open", "--search", "author:app/dependabot", "--limit", "20"],
            options,
            cancellationToken,
            failureIsDegraded: true);
        AppendWorkflowSection(builder, dependabotPrs);

        var auditOutput = audit.Message;
        var vulnerable = ContainsVulnerabilitySignal(auditOutput);
        if (vulnerable)
        {
            builder.AppendLine().AppendLine("Dependency audit found vulnerable package output.");
        }

        var exitCode = audit.ExitCode != 0 || vulnerable ? 1 : 0;
        return new CodingAgentCommandResult(exitCode, builder.ToString().TrimEnd(), false);
    }

    private async Task<CodingAgentCommandResult> RunGithubCommandAsync(
        CodingAgentCommandOptions options,
        CancellationToken cancellationToken)
    {
        var arguments = GetSlashArguments(options.CommandText);
        if (arguments.Count > 0 && IsCommand(arguments[0], "app", "status"))
        {
            if (arguments.Count > 1 && IsCommand(arguments[1], "repos", "repositories", "list"))
            {
                return await RunGitHubAppRepositoriesAsync(cancellationToken);
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "actions", "runs", "workflows", "workflow-runs"))
            {
                return await RunGitHubAppWorkflowRunsAsync(cancellationToken);
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "diagnose", "doctor", "ci"))
            {
                return await RunGitHubAppWorkflowDiagnosisAsync(arguments, 2, cancellationToken);
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "logs", "log"))
            {
                return await RunGitHubAppWorkflowJobLogsAsync(arguments, 2, cancellationToken);
            }

            if (arguments.Count > 1 && IsCommand(arguments[1], "run", "jobs"))
            {
                return await RunGitHubAppWorkflowRunJobsAsync(arguments, 2, cancellationToken);
            }

            return await RunGitHubAppStatusAsync(cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "repos", "repositories", "list"))
        {
            return await RunGitHubAppRepositoriesAsync(cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "actions", "runs", "workflows", "workflow-runs"))
        {
            return await RunGitHubAppWorkflowRunsAsync(cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "diagnose", "doctor", "ci"))
        {
            return await RunGitHubAppWorkflowDiagnosisAsync(arguments, 1, cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "logs", "log"))
        {
            return await RunGitHubAppWorkflowJobLogsAsync(arguments, 1, cancellationToken);
        }

        if (arguments.Count > 0 && IsCommand(arguments[0], "run", "jobs"))
        {
            return await RunGitHubAppWorkflowRunJobsAsync(arguments, 1, cancellationToken);
        }

        return await RunGithubWorkflowAsync(options, cancellationToken);
    }

    private async Task<CodingAgentCommandResult> RunGithubWorkflowAsync(
        CodingAgentCommandOptions options,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .AppendLine("Kam coding-agent GitHub status:");

        var remote = await RunProcessStepAsync(
            "git remotes",
            "git",
            ["remote", "-v"],
            options,
            cancellationToken);
        AppendWorkflowSection(builder, remote);

        var prs = await RunProcessStepAsync(
            "GitHub PR status",
            "gh",
            ["pr", "status"],
            options,
            cancellationToken,
            failureIsDegraded: true);
        AppendWorkflowSection(builder, prs);

        var runs = await RunProcessStepAsync(
            "GitHub Actions runs",
            "gh",
            ["run", "list", "--limit", "5"],
            options,
            cancellationToken,
            failureIsDegraded: true);
        AppendWorkflowSection(builder, runs);

        return new CodingAgentCommandResult(remote.ExitCode == 0 ? 0 : 1, builder.ToString().TrimEnd(), false);
    }

    private async Task<CodingAgentCommandResult> RunGitHubAppStatusAsync(CancellationToken cancellationToken)
    {
        if (_githubAppClient is null)
        {
            return CodingAgentCommandResult.Success(FormatGitHubAppUnavailable());
        }

        var status = await _githubAppClient.GetStatusAsync(cancellationToken);
        return CodingAgentCommandResult.Success(FormatGitHubAppStatus(status));
    }

    private async Task<CodingAgentCommandResult> RunGitHubAppRepositoriesAsync(CancellationToken cancellationToken)
    {
        if (_githubAppClient is null)
        {
            return CodingAgentCommandResult.Success(FormatGitHubAppUnavailable());
        }

        var result = await _githubAppClient.ListRepositoriesAsync(cancellationToken);
        if (!result.Success)
        {
            return CodingAgentCommandResult.Success(FormatGitHubAppSetup(result.Message, result.MissingSettings));
        }

        var builder = new StringBuilder()
            .AppendLine("Kam GitHub App repositories:")
            .AppendLine($"  status: {result.Message}");

        foreach (var repository in result.Repositories
            .OrderBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(50))
        {
            builder.AppendLine(
                $"  - {repository.FullName} ({(repository.IsPrivate ? "private" : "public")}, default: {repository.DefaultBranch})");
        }

        if (result.Repositories.Count > 50)
        {
            builder.AppendLine($"  ... {result.Repositories.Count - 50} more repositories hidden");
        }

        return CodingAgentCommandResult.Success(builder.ToString().TrimEnd());
    }

    private async Task<CodingAgentCommandResult> RunGitHubAppWorkflowRunsAsync(CancellationToken cancellationToken)
    {
        if (_githubAppClient is null)
        {
            return CodingAgentCommandResult.Success(FormatGitHubAppUnavailable());
        }

        var result = await _githubAppClient.ListWorkflowRunsAsync(cancellationToken);
        if (!result.Success)
        {
            return CodingAgentCommandResult.Success(FormatGitHubAppSetup(result.Message, result.MissingSettings));
        }

        var builder = new StringBuilder()
            .AppendLine("Kam GitHub App workflow runs:")
            .AppendLine($"  status: {NormalizeMessage(result.Message)}");

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

        return CodingAgentCommandResult.Success(builder.ToString().TrimEnd());
    }

    private async Task<CodingAgentCommandResult> RunGitHubAppWorkflowDiagnosisAsync(
        IReadOnlyList<string> arguments,
        int firstArgumentIndex,
        CancellationToken cancellationToken)
    {
        if (arguments.Count <= firstArgumentIndex)
        {
            return new CodingAgentCommandResult(2, "Usage: /github diagnose <owner/repo> [runId]", false);
        }

        long? requestedRunId = null;
        if (arguments.Count > firstArgumentIndex + 1)
        {
            if (!long.TryParse(arguments[firstArgumentIndex + 1], out var runId) || runId <= 0)
            {
                return new CodingAgentCommandResult(2, "Usage: /github diagnose <owner/repo> [runId]", false);
            }

            requestedRunId = runId;
        }

        if (_githubAppClient is null)
        {
            return CodingAgentCommandResult.Success(FormatGitHubAppUnavailable());
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
                return CodingAgentCommandResult.Success(
                    FormatGitHubAppSetup(workflowRuns.Message, workflowRuns.MissingSettings));
            }

            selectedRun = SelectWorkflowRunForDiagnosis(workflowRuns.WorkflowRuns, repositoryFullName);
            if (selectedRun is null)
            {
                return CodingAgentCommandResult.Success(
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
            return CodingAgentCommandResult.Success(FormatGitHubAppSetup(jobs.Message, jobs.MissingSettings));
        }

        return CodingAgentCommandResult.Success(FormatWorkflowDiagnosis(jobs, selectedRun, selectionMessage));
    }

    private async Task<CodingAgentCommandResult> RunGitHubAppWorkflowJobLogsAsync(
        IReadOnlyList<string> arguments,
        int firstArgumentIndex,
        CancellationToken cancellationToken)
    {
        if (arguments.Count <= firstArgumentIndex + 1)
        {
            return new CodingAgentCommandResult(2, "Usage: /github logs <owner/repo> <jobId>", false);
        }

        if (!long.TryParse(arguments[firstArgumentIndex + 1], out var jobId) || jobId <= 0)
        {
            return new CodingAgentCommandResult(2, "Usage: /github logs <owner/repo> <jobId>", false);
        }

        if (_githubAppClient is null)
        {
            return CodingAgentCommandResult.Success(FormatGitHubAppUnavailable());
        }

        var repositoryFullName = arguments[firstArgumentIndex];
        var result = await _githubAppClient.GetWorkflowJobLogAsync(
            repositoryFullName,
            jobId,
            cancellationToken);
        if (!result.Success)
        {
            return CodingAgentCommandResult.Success(FormatGitHubAppSetup(result.Message, result.MissingSettings));
        }

        return CodingAgentCommandResult.Success(FormatWorkflowJobLogs(result));
    }

    private async Task<CodingAgentCommandResult> RunGitHubAppWorkflowRunJobsAsync(
        IReadOnlyList<string> arguments,
        int firstArgumentIndex,
        CancellationToken cancellationToken)
    {
        if (arguments.Count <= firstArgumentIndex + 1)
        {
            return new CodingAgentCommandResult(2, "Usage: /github run <owner/repo> <runId>", false);
        }

        if (!long.TryParse(arguments[firstArgumentIndex + 1], out var runId) || runId <= 0)
        {
            return new CodingAgentCommandResult(2, "Usage: /github run <owner/repo> <runId>", false);
        }

        if (_githubAppClient is null)
        {
            return CodingAgentCommandResult.Success(FormatGitHubAppUnavailable());
        }

        var repositoryFullName = arguments[firstArgumentIndex];
        var result = await _githubAppClient.ListWorkflowRunJobsAsync(
            repositoryFullName,
            runId,
            cancellationToken);
        if (!result.Success)
        {
            return CodingAgentCommandResult.Success(FormatGitHubAppSetup(result.Message, result.MissingSettings));
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
                .AppendLine($"  - {NormalizeMessage(job.Name)} ({FormatWorkflowJobState(job)})")
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

        return CodingAgentCommandResult.Success(builder.ToString().TrimEnd());
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

        builder
            .AppendLine("  setup:")
            .AppendLine("    dotnet user-secrets set \"GitHubApp:AppId\" \"<app-id>\"")
            .AppendLine("    dotnet user-secrets set \"GitHubApp:InstallationId\" \"<installation-id>\"")
            .AppendLine("    dotnet user-secrets set \"GitHubApp:PrivateKeyPath\" \"<absolute-pem-path>\"")
            .AppendLine("  list repos: kam coding-agent /github repos")
            .AppendLine("  list workflow runs: kam coding-agent /github actions")
            .AppendLine("  diagnose workflow run: kam coding-agent /github diagnose <owner/repo> [runId]")
            .AppendLine("  workflow job logs: kam coding-agent /github logs <owner/repo> <jobId>")
            .AppendLine("  list workflow run jobs: kam coding-agent /github run <owner/repo> <runId>")
            .AppendLine("  private key contents and installation tokens are never printed.");

        return builder.ToString().TrimEnd();
    }

    private async Task<CodingAgentCommandResult> FormatPluginsAsync(CancellationToken cancellationToken)
    {
        if (_skillHealthService is null)
        {
            return CodingAgentCommandResult.Success("Kam plugins: skill health service is not available.");
        }

        var reports = await _skillHealthService.GetHealthAsync(cancellationToken);
        var builder = new StringBuilder()
            .AppendLine("Kam plugins/skills:");

        foreach (var group in reports
            .GroupBy(report => report.Status)
            .OrderBy(group => group.Key.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"  {group.Key}: {group.Count()}");
        }

        var nonHealthy = reports
            .Where(report => report.Status != SkillHealthStatus.Healthy)
            .OrderBy(report => report.SkillId, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        if (nonHealthy.Length == 0)
        {
            builder.AppendLine("  all registered skills are healthy");
        }
        else
        {
            builder.AppendLine("  attention:");
            foreach (var report in nonHealthy)
            {
                builder.AppendLine($"    {report.SkillId}: {report.Status} - {NormalizeMessage(report.Details)}");
            }
        }

        return CodingAgentCommandResult.Success(builder.ToString().TrimEnd());
    }

    private string FormatMcp()
    {
        return string.Join(Environment.NewLine, [
            "Kam MCP status:",
            $"  Todoist endpoint: {FormatConfiguredValue(_mcpOptions.TodoistServerLink)}",
            $"  Todoist API key: {FormatSecretStatus(_mcpOptions.TodoistApiKey)}",
            "  MCP skill adapter: available through skill import sources"
        ]);
    }

    private string FormatAgents()
    {
        var names = _agentRegistry?.GetAllAgentNames()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        var builder = new StringBuilder()
            .AppendLine("Kam agents:");

        if (names.Length == 0)
        {
            builder.AppendLine("  registered runtime agents: none");
        }
        else
        {
            builder.AppendLine("  registered runtime agents:");
            foreach (var name in names)
            {
                builder.AppendLine($"    {name}");
            }
        }

        builder
            .AppendLine("  coding role templates:")
            .AppendLine("    reviewer")
            .AppendLine("    test-runner")
            .AppendLine("    dependency-auditor")
            .AppendLine("    plugin-operator")
            .AppendLine("    worktree-planner");

        return builder.ToString().TrimEnd();
    }

    private async Task<CodingAgentCommandResult> RunWorktreeCommandAsync(
        CodingAgentCommandOptions options,
        CancellationToken cancellationToken)
    {
        var arguments = GetSlashArguments(options.CommandText);
        if (arguments.Count == 0 || IsCommand(arguments[0], "list", "status"))
        {
            return await RunProcessStepAsync(
                "git worktree list",
                "git",
                ["worktree", "list", "--porcelain"],
                options,
                cancellationToken);
        }

        if (IsCommand(arguments[0], "plan"))
        {
            var branchName = arguments.Count > 1 ? arguments[1] : "feature/<name>";
            return CodingAgentCommandResult.Success(string.Join(Environment.NewLine, [
                "Kam worktree plan:",
                $"  branch: {branchName}",
                "  create: git worktree add <sibling-path> " + branchName,
                "  remove: git worktree remove <sibling-path>",
                "  prune: git worktree prune"
            ]));
        }

        if (IsCommand(arguments[0], "add"))
        {
            return await RunWorktreeAddCommandAsync(options, arguments, cancellationToken);
        }

        return new CodingAgentCommandResult(2, $"Unknown worktree command: {options.CommandText}", false);
    }

    private async Task<CodingAgentCommandResult> RunWorktreeAddCommandAsync(
        CodingAgentCommandOptions options,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (!arguments.Any(argument => argument.Equals(WorktreeExecuteFlag, StringComparison.OrdinalIgnoreCase)))
        {
            return new CodingAgentCommandResult(
                2,
                "Worktree creation is intentionally not wired without explicit execution confirmation. Add --execute after /worktree add to run git worktree add.",
                false);
        }

        var values = arguments
            .Skip(1)
            .Where(argument => !argument.Equals(WorktreeExecuteFlag, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (values.Length < 2)
        {
            return new CodingAgentCommandResult(
                2,
                "Usage: /worktree add --execute <sibling-path> <branch>",
                false);
        }

        if (!TryResolveWorktreePath(options.WorkspaceRoot, values[0], out var worktreePath))
        {
            return new CodingAgentCommandResult(
                2,
                "Worktree path must stay under the workspace parent.",
                false);
        }

        var branchName = values[1];
        if (!IsSafeWorktreeBranchName(branchName))
        {
            return new CodingAgentCommandResult(
                2,
                "Worktree branch name contains unsupported characters.",
                false);
        }

        return await RunProcessStepAsync(
            "git worktree add",
            "git",
            ["worktree", "add", worktreePath, branchName],
            options,
            cancellationToken,
            timeout: ReviewStepTimeout);
    }

    private static string FormatHooks(CodingAgentCommandOptions options)
    {
        return string.Join(Environment.NewLine, [
            "Kam coding-agent hooks:",
            $"  enabled: {options.HooksEnabled}",
            $"  pre-test: {FormatHookList(options.PreTestHooks)}",
            $"  post-test: {FormatHookList(options.PostTestHooks)}",
            $"  pre-review: {FormatHookList(options.PreReviewHooks)}",
            $"  post-review: {FormatHookList(options.PostReviewHooks)}"
        ]);
    }

    private async Task<CodingAgentCommandResult> RunProcessStepAsync(
        string label,
        string fileName,
        IReadOnlyList<string> arguments,
        CodingAgentCommandOptions options,
        CancellationToken cancellationToken,
        bool failureIsDegraded = false,
        TimeSpan? timeout = null)
    {
        var result = await RunProcessStepWithRawAsync(
            label,
            fileName,
            arguments,
            options,
            cancellationToken,
            failureIsDegraded,
            timeout);

        return result.Step;
    }

    private async Task<(CodingAgentCommandResult Step, CodingAgentProcessResult Raw)> RunProcessStepWithRawAsync(
        string label,
        string fileName,
        IReadOnlyList<string> arguments,
        CodingAgentCommandOptions options,
        CancellationToken cancellationToken,
        bool failureIsDegraded = false,
        TimeSpan? timeout = null)
    {
        var result = await _processRunner.RunAsync(
            new CodingAgentProcessRequest(
                fileName,
                arguments,
                options.WorkspaceRoot,
                timeout ?? StatusStepTimeout),
            cancellationToken);

        var status = result.TimedOut
            ? "TIMEOUT"
            : result.Success ? "PASS" : failureIsDegraded ? "DEGRADED" : "FAIL";
        var command = string.Join(' ', new[] { fileName }.Concat(arguments));
        var output = result.CombinedOutput();
        var message = new StringBuilder()
            .AppendLine($"[{status}] {label}")
            .AppendLine($"  command: {command}")
            .AppendLine($"  exitCode: {result.ExitCode}");

        if (!string.IsNullOrWhiteSpace(output))
        {
            message.AppendLine(output.TrimEnd());
        }

        var exitCode = result.Success
            ? 0
            : result.ExitCode == 127 ? 2
            : failureIsDegraded ? 0
            : 1;
        return (new CodingAgentCommandResult(exitCode, message.ToString().TrimEnd(), false), result);
    }

    private static void AppendWorkflowSection(StringBuilder builder, CodingAgentCommandResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
        {
            return;
        }

        builder
            .AppendLine()
            .AppendLine(result.Message.TrimEnd());
    }

    private static async Task<string> RunGitAsync(
        string workspaceRoot,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workspaceRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return "git: process could not be started";
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            if (process.ExitCode == 0)
            {
                return stdout;
            }

            var stderr = await stderrTask;
            return $"git: {NormalizeMessage(stderr)}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return $"git: {ex.Message}";
        }
    }

    private static async Task WriteSummaryAsync(
        CodingAgentCommandOptions options,
        CodingAgentCommandResult result,
        string summaryPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(summaryPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var markdown = new StringBuilder()
            .AppendLine("# Coding Agent Command")
            .AppendLine()
            .AppendLine($"- timestamp: {DateTimeOffset.Now:o}")
            .AppendLine($"- command: {NormalizeMessage(options.CommandText)}")
            .AppendLine($"- workspace: {NormalizeMessage(options.WorkspaceRoot)}")
            .AppendLine($"- approvalMode: {NormalizeMessage(options.ApprovalMode)}")
            .AppendLine($"- exitCode: {result.ExitCode}")
            .AppendLine($"- requiresConfirmation: {result.RequiresConfirmation}")
            .AppendLine($"- message: {NormalizeMessage(result.Message)}")
            .ToString();

        await File.WriteAllTextAsync(summaryPath, markdown, cancellationToken);
    }

    private static bool TryResolveWorkspacePath(
        string workspaceRoot,
        string path,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;

        try
        {
            var root = Path.GetFullPath(workspaceRoot);
            var candidate = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(root, path));

            if (IsSameOrChildPath(root, candidate))
            {
                resolvedPath = candidate;
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return false;
    }

    private static bool TryResolveWorktreePath(
        string workspaceRoot,
        string path,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var workspace = Path.GetFullPath(workspaceRoot);
            var parent = Directory.GetParent(workspace)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
            {
                return false;
            }

            var candidate = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workspace, path));

            if (!IsSameOrChildPath(parent, candidate))
            {
                return false;
            }

            if (IsSameOrChildPath(workspace, candidate))
            {
                return false;
            }

            resolvedPath = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSameOrChildPath(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(
                normalizedRoot + Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsVulnerabilitySignal(string output)
    {
        return output.Contains("has the following vulnerable packages", StringComparison.OrdinalIgnoreCase)
            || (output.Contains("Transitive Package", StringComparison.OrdinalIgnoreCase)
                && output.Contains("Severity", StringComparison.OrdinalIgnoreCase))
            || (output.Contains("Top-level Package", StringComparison.OrdinalIgnoreCase)
                && output.Contains("Severity", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetSlashArguments(string commandText)
    {
        return commandText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .ToArray();
    }

    private static bool IsCommand(string value, params string[] names)
    {
        return names.Any(name => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafeWorktreeBranchName(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName)
            || branchName.StartsWith("-", StringComparison.Ordinal)
            || branchName.Contains("..", StringComparison.Ordinal)
            || branchName.Contains('\\', StringComparison.Ordinal)
            || branchName.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return branchName.All(character =>
            char.IsLetterOrDigit(character)
            || character is '/' or '-' or '_' or '.');
    }

    private static string FormatHookList(IReadOnlyList<string> hooks)
    {
        return hooks.Count == 0 ? "(none)" : string.Join(" | ", hooks.Select(NormalizeMessage));
    }

    private static string FormatConfiguredValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(not configured)" : NormalizeMessage(value);
    }

    private static string FormatSecretStatus(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(not configured)" : "(configured)";
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

    private static string NormalizeWorkspaceRoot(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return Environment.CurrentDirectory;
        }

        try
        {
            return Path.GetFullPath(workspaceRoot.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return workspaceRoot.Trim();
        }
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

    private static bool IsValueSwitch(string arg, params string[] names)
    {
        return names.Any(name => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record CodingAgentCommandResult(
        int ExitCode,
        string Message,
        bool RequiresConfirmation)
    {
        public static CodingAgentCommandResult Success(string message)
        {
            return new CodingAgentCommandResult(0, message, false);
        }
    }
}
