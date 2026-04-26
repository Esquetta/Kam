using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Policy;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class ShellSkillExecutor : ISkillExecutor
{
    private const int DefaultTimeoutMilliseconds = 10000;
    private const int MaxTimeoutMilliseconds = 15000;
    private const int DefaultMaxOutputLength = 6000;
    private const int MaxOutputLength = 20000;

    private static readonly string[] BlockedPatterns =
    [
        "git reset --hard",
        "git clean -fd",
        "rm -rf",
        "remove-item",
        "del /s",
        "rd /s",
        "rmdir /s",
        "format ",
        "diskpart",
        "shutdown",
        "restart-computer",
        "stop-computer",
        "mkfs",
        "dd if=",
        ":(){"
    ];

    private readonly ISkillRegistry? _skillRegistry;

    public ShellSkillExecutor(ISkillRegistry? skillRegistry = null)
    {
        _skillRegistry = skillRegistry;
    }

    public bool CanExecute(string skillId)
    {
        return skillId.Equals("shell.run", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(plan.SkillId))
        {
            return SkillResult.Failed($"Unsupported shell skill: {plan.SkillId}");
        }

        var command = SkillPlanArgumentReader.GetString(plan, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return SkillResult.Failed(
                "Argument 'command' is required.",
                SkillExecutionStatus.ValidationFailed,
                "validation_failed");
        }

        var runtimeOptions = GetRuntimeOptions(plan.SkillId);
        var policyFailure = ValidateRuntimePolicy(command, runtimeOptions);
        if (policyFailure is not null)
        {
            return policyFailure;
        }

        var workingDirectory = SkillPlanArgumentReader.GetString(
            plan,
            "workingDirectory",
            Environment.CurrentDirectory);
        if (!Directory.Exists(workingDirectory))
        {
            return SkillResult.Failed(
                $"Working directory '{workingDirectory}' does not exist.",
                SkillExecutionStatus.ValidationFailed,
                "working_directory_not_found");
        }

        var timeoutMilliseconds = Math.Clamp(
            SkillPlanArgumentReader.GetInt(plan, "timeoutMilliseconds", DefaultTimeoutMilliseconds),
            1000,
            MaxTimeoutMilliseconds);
        var maxOutputLength = Math.Clamp(
            SkillPlanArgumentReader.GetInt(plan, "maxOutputLength", DefaultMaxOutputLength),
            500,
            MaxOutputLength);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(command, workingDirectory),
            EnableRaisingEvents = false
        };

        try
        {
            if (!process.Start())
            {
                return SkillResult.Failed("Shell process could not be started.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMilliseconds);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKillProcess(process);
                return SkillResult.Failed(
                    $"Shell command timed out after {timeoutMilliseconds} ms.",
                    SkillExecutionStatus.TimedOut,
                    "timeout");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var message = FormatResult(process.ExitCode, stdout, stderr, maxOutputLength);

            return process.ExitCode == 0
                ? SkillResult.Succeeded(message)
                : SkillResult.Failed(message, SkillExecutionStatus.Failed, "shell_exit_code");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            return SkillResult.Failed(
                "Shell command was cancelled.",
                SkillExecutionStatus.Cancelled,
                "cancelled");
        }
        catch (Exception ex)
        {
            return SkillResult.Failed(
                $"Shell command failed: {ex.Message}",
                SkillExecutionStatus.Failed,
                "shell_exception");
        }
    }

    private static ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        return new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "/bin/sh",
            Arguments = isWindows
                ? $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command {QuotePowerShell(command)}"
                : $"-c {QuotePosix(command)}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    private IReadOnlyDictionary<string, string> GetRuntimeOptions(string skillId)
    {
        return _skillRegistry is not null
            && _skillRegistry.TryGet(skillId, out var manifest)
            && manifest is not null
            ? manifest.RuntimeOptions
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static SkillResult? ValidateRuntimePolicy(
        string command,
        IReadOnlyDictionary<string, string> runtimeOptions)
    {
        if (IsBlockedCommand(command, runtimeOptions))
        {
            return SkillResult.Failed(
                "Shell command blocked by safety policy.",
                SkillExecutionStatus.PermissionDenied,
                "shell_command_blocked");
        }

        if (!IsAllowedCommand(command, runtimeOptions))
        {
            return SkillResult.Failed(
                "Shell command is not in the configured allow list.",
                SkillExecutionStatus.PermissionDenied,
                "shell_command_not_allowed");
        }

        return null;
    }

    private static bool IsBlockedCommand(
        string command,
        IReadOnlyDictionary<string, string> runtimeOptions)
    {
        var normalized = NormalizeCommand(command);
        var blockedPatterns = BlockedPatterns
            .Concat(GetRuntimeList(runtimeOptions, SkillRuntimePolicyOptions.ShellBlockedPatterns));

        return blockedPatterns.Any(pattern =>
            normalized.Contains(NormalizeCommand(pattern), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowedCommand(
        string command,
        IReadOnlyDictionary<string, string> runtimeOptions)
    {
        var allowedCommands = GetRuntimeList(runtimeOptions, SkillRuntimePolicyOptions.ShellAllowedCommands);
        if (allowedCommands.Count == 0)
        {
            return true;
        }

        var normalized = NormalizeCommand(command);
        return allowedCommands.Any(commandPrefix =>
            normalized.StartsWith(NormalizeCommand(commandPrefix), StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyCollection<string> GetRuntimeList(
        IReadOnlyDictionary<string, string> runtimeOptions,
        string key)
    {
        return runtimeOptions.TryGetValue(key, out var value)
            ? SkillRuntimePolicyOptions.SplitList(value)
            : [];
    }

    private static string NormalizeCommand(string command)
    {
        return Regex.Replace(command.Trim().ToLowerInvariant(), @"\s+", " ");
    }

    private static string FormatResult(
        int exitCode,
        string stdout,
        string stderr,
        int maxOutputLength)
    {
        var output = NormalizeOutput(stdout, stderr);
        var truncated = output.Length > maxOutputLength;
        if (truncated)
        {
            output = output[..maxOutputLength];
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Exit Code: {exitCode}");
        builder.AppendLine("Output:");
        builder.AppendLine(string.IsNullOrWhiteSpace(output) ? "(no output)" : output.TrimEnd());
        if (truncated)
        {
            builder.AppendLine("[truncated]");
        }

        return builder.ToString();
    }

    private static string NormalizeOutput(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return stdout;
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return stderr;
        }

        return $"{stdout.TrimEnd()}{Environment.NewLine}{stderr.TrimEnd()}";
    }

    private static string QuotePowerShell(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }

    private static string QuotePosix(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Timeout/cancellation path already returns a normalized result.
        }
    }
}
