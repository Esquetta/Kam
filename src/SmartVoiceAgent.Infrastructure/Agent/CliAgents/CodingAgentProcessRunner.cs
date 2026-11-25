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
/// <remarks>
/// Output is collected as it arrives. Once the process exits, or is killed, the runner waits only a short grace
/// period for the rest: a child the process left behind (a dev server, a watcher) can hold the output pipe open
/// forever, and the caller gets what was captured instead of waiting on it.
/// </remarks>
public sealed class CodingAgentProcessRunner : ICodingAgentProcessRunner
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeSpan _outputGracePeriod;

    /// <summary>
    /// Creates the runner.
    /// </summary>
    /// <param name="outputGracePeriod">How long to keep reading output after the process ends; defaults to 2 seconds.</param>
    public CodingAgentProcessRunner(TimeSpan? outputGracePeriod = null)
    {
        _outputGracePeriod = outputGracePeriod ?? TimeSpan.FromSeconds(2);
    }

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

            var stdout = OutputCapture.Start(process.StandardOutput);
            var stderr = OutputCapture.Start(process.StandardError);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(request.Timeout);

            try
            {
                if (request.StandardInput is not null)
                {
                    await WriteInputAsync(process, request.StandardInput, timeoutCts.Token);
                }

                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw;
            }
            catch (OperationCanceledException)
            {
                // Kill(entireProcessTree) misses descendants whose parent already exited, so the reads below stop
                // after the grace period rather than at the end of the pipe.
                TryKill(process);
                await WaitForOutputAsync(stdout, stderr, cancellationToken);
                return new CodingAgentProcessResult(-1, stdout.Text, stderr.Text, true);
            }

            await WaitForOutputAsync(stdout, stderr, cancellationToken);
            return new CodingAgentProcessResult(process.ExitCode, stdout.Text, stderr.Text, false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return new CodingAgentProcessResult(127, string.Empty, ex.Message, false);
        }
    }

    private static async Task WriteInputAsync(Process process, string input, CancellationToken cancellationToken)
    {
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process exited before reading its input; its exit code and output tell the caller why.
        }
    }

    private async Task WaitForOutputAsync(OutputCapture stdout, OutputCapture stderr, CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(stdout.Completion, stderr.Completion).WaitAsync(_outputGracePeriod, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Something the process started still holds the pipe; return what arrived so far.
        }
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

    /// <summary>
    /// Reads a stream into a buffer in the background, so the text read so far is available at any time.
    /// </summary>
    private sealed class OutputCapture
    {
        private readonly StringBuilder _text = new();

        private OutputCapture()
        {
        }

        public Task Completion { get; private set; } = Task.CompletedTask;

        public string Text
        {
            get
            {
                lock (_text)
                {
                    return _text.ToString();
                }
            }
        }

        public static OutputCapture Start(StreamReader reader)
        {
            var capture = new OutputCapture();
            capture.Completion = Task.Run(() => capture.ReadAsync(reader));
            return capture;
        }

        private async Task ReadAsync(StreamReader reader)
        {
            var buffer = new char[4096];
            try
            {
                int read;
                while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None)) > 0)
                {
                    lock (_text)
                    {
                        _text.Append(buffer, 0, read);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The process was disposed while a child still held the pipe; keep what was read.
            }
        }
    }
}
