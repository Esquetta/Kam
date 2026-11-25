using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Agent.CliAgents;

/// <summary>
/// Runs a process to completion with a timeout, capturing its output.
/// </summary>
public interface ICodingAgentProcessRunner
{
    /// <summary>
    /// Runs the process. Cancelling the token kills the process tree and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="request">What to run.</param>
    /// <param name="cancellationToken">Stops the process.</param>
    Task<CodingAgentProcessResult> RunAsync(
        CodingAgentProcessRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A process to run.
/// </summary>
/// <param name="FileName">The executable.</param>
/// <param name="Arguments">Arguments, passed without shell parsing.</param>
/// <param name="WorkingDirectory">The folder the process starts in.</param>
/// <param name="Timeout">How long the process may run before it is killed.</param>
/// <param name="StandardInput">Text written to the process's standard input, which is then closed; null leaves it unredirected.</param>
public sealed record CodingAgentProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan Timeout,
    string? StandardInput = null);

/// <summary>
/// The outcome of a process run.
/// </summary>
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

/// <summary>
/// Runs processes with <see cref="Process"/>, reading output as UTF-8.
/// </summary>
public sealed class CodingAgentProcessRunner : ICodingAgentProcessRunner
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
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
                RedirectStandardInput = request.StandardInput is not null,
                StandardOutputEncoding = Utf8,
                StandardErrorEncoding = Utf8,
                StandardInputEncoding = request.StandardInput is not null ? Utf8 : null,
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

            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            if (request.StandardInput is not null)
            {
                try
                {
                    await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), CancellationToken.None);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The process exited before reading its input; its exit code and output tell the caller why.
                }
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(request.Timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw;
            }
            catch (OperationCanceledException)
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
