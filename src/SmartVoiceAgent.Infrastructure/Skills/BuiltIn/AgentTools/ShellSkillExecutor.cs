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
        "rm ",
        "unlink ",
        "remove-item",
        "del ",
        "del /s",
        "erase ",
        "rd /s",
        "rmdir /s",
        "cmd /c del",
        "cmd /c erase",
        "cmd /c rd",
        "cmd /c rmdir",
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

        var workingDirectoryArgument = SkillPlanArgumentReader.GetString(
            plan,
            "workingDirectory",
            Environment.CurrentDirectory);
        var workingDirectoryFailure = ValidateWorkingDirectory(
            workingDirectoryArgument,
            runtimeOptions,
            out var workingDirectory);
        if (workingDirectoryFailure is not null)
        {
            return workingDirectoryFailure;
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

        var stopwatch = Stopwatch.StartNew();
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
                var timeoutData = CreateResult(
                    command,
                    workingDirectory,
                    exitCode: null,
                    await ReadCompletedOrEmptyAsync(stdoutTask),
                    await ReadCompletedOrEmptyAsync(stderrTask),
                    timedOut: true,
                    cancelled: false,
                    maxOutputLength,
                    stopwatch.ElapsedMilliseconds);

                return Failed(
                    $"Shell command timed out after {timeoutMilliseconds} ms.",
                    SkillExecutionStatus.TimedOut,
                    "timeout",
                    timeoutData);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var data = CreateResult(
                command,
                workingDirectory,
                process.ExitCode,
                stdout,
                stderr,
                timedOut: false,
                cancelled: false,
                maxOutputLength,
                stopwatch.ElapsedMilliseconds);
            var message = FormatResult(data);

            return process.ExitCode == 0
                ? SkillResult.Succeeded(message, data) with { DurationMilliseconds = data.DurationMilliseconds }
                : Failed(message, SkillExecutionStatus.Failed, "shell_exit_code", data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            var cancelledData = CreateResult(
                command,
                workingDirectory,
                exitCode: null,
                string.Empty,
                string.Empty,
                timedOut: false,
                cancelled: true,
                maxOutputLength,
                stopwatch.ElapsedMilliseconds);

            return Failed(
                "Shell command was cancelled.",
                SkillExecutionStatus.Cancelled,
                "cancelled",
                cancelledData);
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
        var startInfo = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "/bin/sh",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (isWindows)
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }

        return startInfo;
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

    private static SkillResult? ValidateWorkingDirectory(
        string workingDirectory,
        IReadOnlyDictionary<string, string> runtimeOptions,
        out string resolvedWorkingDirectory)
    {
        try
        {
            resolvedWorkingDirectory = Path.GetFullPath(
                string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.CurrentDirectory
                    : workingDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            resolvedWorkingDirectory = string.Empty;
            return SkillResult.Failed(
                $"Working directory path is invalid: {ex.Message}",
                SkillExecutionStatus.ValidationFailed,
                "working_directory_invalid");
        }

        if (!Directory.Exists(resolvedWorkingDirectory))
        {
            return SkillResult.Failed(
                $"Working directory '{resolvedWorkingDirectory}' does not exist.",
                SkillExecutionStatus.ValidationFailed,
                "working_directory_not_found");
        }

        var allowedDirectories = GetRuntimeList(
            runtimeOptions,
            SkillRuntimePolicyOptions.ShellAllowedWorkingDirectories);
        if (allowedDirectories.Count == 0)
        {
            return null;
        }

        var normalizedWorkingDirectory = resolvedWorkingDirectory;
        return allowedDirectories.Any(root => IsSameOrChildDirectory(normalizedWorkingDirectory, root))
            ? null
            : SkillResult.Failed(
                "Shell working directory is not in the configured allowed directory list.",
                SkillExecutionStatus.PermissionDenied,
                "shell_working_directory_not_allowed");
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

    private static ShellCommandResult CreateResult(
        string command,
        string workingDirectory,
        int? exitCode,
        string stdout,
        string stderr,
        bool timedOut,
        bool cancelled,
        int maxOutputLength,
        long durationMilliseconds)
    {
        var (limitedStdout, limitedStderr, truncated) = LimitOutput(stdout, stderr, maxOutputLength);

        return new ShellCommandResult
        {
            Command = command,
            WorkingDirectory = workingDirectory,
            ExitCode = exitCode,
            StdOut = limitedStdout,
            StdErr = limitedStderr,
            TimedOut = timedOut,
            Cancelled = cancelled,
            Truncated = truncated,
            MaxOutputLength = maxOutputLength,
            DurationMilliseconds = durationMilliseconds
        };
    }

    private static string FormatResult(ShellCommandResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Exit Code: {result.ExitCode?.ToString() ?? "(not available)"}");
        builder.AppendLine($"Working Directory: {result.WorkingDirectory}");
        builder.AppendLine("Stdout:");
        builder.AppendLine(string.IsNullOrWhiteSpace(result.StdOut) ? "(no stdout)" : result.StdOut.TrimEnd());
        builder.AppendLine("Stderr:");
        builder.AppendLine(string.IsNullOrWhiteSpace(result.StdErr) ? "(no stderr)" : result.StdErr.TrimEnd());
        if (result.TimedOut)
        {
            builder.AppendLine("[timed out]");
        }

        if (result.Cancelled)
        {
            builder.AppendLine("[cancelled]");
        }

        if (result.Truncated)
        {
            builder.AppendLine("[truncated]");
        }

        return builder.ToString();
    }

    private static (string Stdout, string Stderr, bool Truncated) LimitOutput(
        string stdout,
        string stderr,
        int maxOutputLength)
    {
        stdout ??= string.Empty;
        stderr ??= string.Empty;

        if (stdout.Length + stderr.Length <= maxOutputLength)
        {
            return (stdout, stderr, false);
        }

        var remaining = Math.Max(0, maxOutputLength);
        var limitedStdout = stdout.Length <= remaining
            ? stdout
            : stdout[..remaining];
        remaining -= limitedStdout.Length;

        var limitedStderr = remaining > 0
            ? stderr[..Math.Min(stderr.Length, remaining)]
            : string.Empty;

        return (limitedStdout, limitedStderr, true);
    }

    private static SkillResult Failed(
        string message,
        SkillExecutionStatus status,
        string errorCode,
        ShellCommandResult data)
    {
        return new SkillResult(false, string.Empty, message, data)
        {
            Status = status,
            ErrorCode = errorCode,
            DurationMilliseconds = data.DurationMilliseconds
        };
    }

    private static async Task<string> ReadCompletedOrEmptyAsync(Task<string> outputTask)
    {
        try
        {
            return await outputTask.WaitAsync(TimeSpan.FromMilliseconds(250));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsSameOrChildDirectory(string directory, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            var normalizedDirectory = Path.GetFullPath(directory);
            var normalizedRoot = Path.GetFullPath(root);
            var relative = Path.GetRelativePath(normalizedRoot, normalizedDirectory);
            return relative.Equals(".", StringComparison.Ordinal)
                || (!relative.Equals("..", StringComparison.Ordinal)
                    && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
                    && !Path.IsPathRooted(relative));
        }
        catch
        {
            return false;
        }
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
