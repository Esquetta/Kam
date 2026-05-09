using System.ComponentModel;
using System.Diagnostics;

namespace SmartVoiceAgent.AgentHost.ConsoleApp;

public interface ICodingAgentProcessRunner
{
    Task<CodingAgentProcessResult> RunAsync(
        CodingAgentProcessRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CodingAgentProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan Timeout);

public sealed record CodingAgentProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;

    public string CombinedOutput(int maxLength = 6000)
    {
        var combined = string.Join(
            Environment.NewLine,
            new[] { StandardOutput, StandardError }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();

        if (combined.Length <= maxLength)
        {
            return combined;
        }

        return combined[..maxLength] + Environment.NewLine + "...";
    }
}

public sealed class CodingAgentProcessRunner : ICodingAgentProcessRunner
{
    public async Task<CodingAgentProcessResult> RunAsync(
        CodingAgentProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                WorkingDirectory = request.WorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in request.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return new CodingAgentProcessResult(127, string.Empty, "Process could not be started.", false);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(request.Timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                var timedOutOutput = await ReadCompletedOutputAsync(stdoutTask, stderrTask);
                return new CodingAgentProcessResult(
                    -1,
                    timedOutOutput.StandardOutput,
                    timedOutOutput.StandardError,
                    true);
            }

            var output = await ReadCompletedOutputAsync(stdoutTask, stderrTask);
            return new CodingAgentProcessResult(
                process.ExitCode,
                output.StandardOutput,
                output.StandardError,
                false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return new CodingAgentProcessResult(127, string.Empty, ex.Message, false);
        }
    }

    private static async Task<(string StandardOutput, string StandardError)> ReadCompletedOutputAsync(
        Task<string> stdoutTask,
        Task<string> stderrTask)
    {
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (stdout, stderr);
    }

    private static void TryKill(Process process)
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
            // Best-effort cleanup; timeout is still reported to the caller.
        }
    }
}
