using System.Diagnostics;
using System.Text;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.CodingAgent;
using SmartVoiceAgent.Core.Models.Commands;

namespace SmartVoiceAgent.AgentHost.ConsoleApp;

public sealed class CodingAgentCommandOptions
{
    public string CommandText { get; init; } = CodingAgentCommand.DefaultCommandText;

    public string WorkspaceRoot { get; init; } = Environment.CurrentDirectory;

    public string ApprovalMode { get; init; } = "workspace-write";

    public string? SummaryPath { get; init; }
}

public sealed class CodingAgentCommand
{
    public const string SwitchName = "--coding-agent";
    public const string DefaultCommandText = "/status";

    private readonly ICommandRuntimeService _runtime;

    public CodingAgentCommand(ICommandRuntimeService runtime)
    {
        _runtime = runtime;
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
            SummaryPath = string.IsNullOrWhiteSpace(summaryPath) ? null : summaryPath
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

        var result = options.CommandText.StartsWith("/", StringComparison.Ordinal)
            ? await RunSlashCommandAsync(options, output, cancellationToken)
            : await RunRuntimeCommandAsync(options, output, error, cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.SummaryPath))
        {
            await WriteSummaryAsync(options, result, cancellationToken);
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

        var message = commandName switch
        {
            "/help" => FormatHelp(),
            "/status" => await FormatStatusAsync(options.WorkspaceRoot, cancellationToken),
            "/permissions" => FormatPermissions(options),
            "/diff" => await FormatDiffAsync(options.WorkspaceRoot, cancellationToken),
            "/review" => "The /review command is registered but is not wired to a review workflow in this MVP.",
            "/test" => "The /test command is registered but is not wired to a test workflow in this MVP.",
            _ => $"Unknown coding command: {options.CommandText}"
        };

        await output.WriteLineAsync(message);

        return new CodingAgentCommandResult(
            commandName is "/help" or "/status" or "/permissions" or "/diff" ? 0 : 2,
            message,
            false);
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
            "  /review        Reserved for the review workflow.",
            "  /test          Reserved for the test workflow.",
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
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var gitStatus = await RunGitAsync(workspaceRoot, "status --short --branch", cancellationToken);
        return string.Join(Environment.NewLine, [
            "Kam coding-agent status:",
            $"  workspace: {workspaceRoot}",
            string.IsNullOrWhiteSpace(gitStatus) ? "  git: no status output" : gitStatus.TrimEnd()
        ]);
    }

    private static async Task<string> FormatDiffAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var unstaged = await RunGitAsync(workspaceRoot, "diff --stat", cancellationToken);
        var staged = await RunGitAsync(workspaceRoot, "diff --cached --stat", cancellationToken);
        var combined = string.Join(
            Environment.NewLine,
            new[] { unstaged, staged }.Where(item => !string.IsNullOrWhiteSpace(item))).Trim();

        return string.IsNullOrWhiteSpace(combined)
            ? "No working tree diff."
            : combined;
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
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(options.SummaryPath);
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

        await File.WriteAllTextAsync(options.SummaryPath!, markdown, cancellationToken);
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

    private sealed record CodingAgentCommandResult(
        int ExitCode,
        string Message,
        bool RequiresConfirmation);
}
