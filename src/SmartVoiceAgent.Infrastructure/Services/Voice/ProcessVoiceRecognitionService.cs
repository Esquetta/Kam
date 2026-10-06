using System.Diagnostics;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// Records by reading raw 16 kHz mono 16-bit PCM from a command-line recorder's standard output.
/// </summary>
public abstract class ProcessVoiceRecognitionService : VoiceRecognitionServiceBase
{
    private Process? _process;
    private CancellationTokenSource? _stop;
    private Task? _reader;

    /// <summary>
    /// Returns the recorder to start and its arguments.
    /// </summary>
    protected abstract ProcessStartInfo CreateStartInfo();

    /// <inheritdoc />
    protected override void StartListeningInternal()
    {
        var startInfo = CreateStartInfo();
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"'{startInfo.FileName}' could not be started.");
        var stop = new CancellationTokenSource();
        _process = process;
        _stop = stop;
        _ = process.StandardError.ReadToEndAsync(stop.Token);
        _reader = Task.Run(() => ReadAsync(process, stop.Token));
    }

    /// <inheritdoc />
    protected override void StopListeningInternal()
    {
        _stop?.Cancel();
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill();
                _process.WaitForExit(1000);
            }
        }
        catch (InvalidOperationException)
        {
            // The recorder already exited.
        }
    }

    /// <inheritdoc />
    protected override void CleanupPlatformResources()
    {
        _stop?.Cancel();
        _stop?.Dispose();
        _stop = null;
        _process?.Dispose();
        _process = null;
        _reader = null;
    }

    private async Task ReadAsync(Process process, CancellationToken cancellationToken)
    {
        var buffer = new byte[3200];
        var carry = 0;
        try
        {
            var stream = process.StandardOutput.BaseStream;
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(carry), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var total = carry + read;
                var even = total & ~1;
                AddAudioData(buffer.AsSpan(0, even));
                carry = total - even;
                if (carry > 0)
                {
                    buffer[0] = buffer[even];
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                ReportError(new InvalidOperationException($"The recorder stopped unexpectedly (exit code {SafeExitCode(process)})."));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ReportError(ex);
        }
    }

    private static string SafeExitCode(Process process)
    {
        try
        {
            return process.WaitForExit(500) ? process.ExitCode.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : "running";
        }
        catch (InvalidOperationException)
        {
            return "unknown";
        }
    }
}

/// <summary>
/// Records on Linux with ALSA's <c>arecord</c>.
/// </summary>
public sealed class LinuxVoiceRecognitionService : ProcessVoiceRecognitionService
{
    /// <inheritdoc />
    protected override ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo("arecord");
        foreach (var argument in new[] { "-q", "-f", "S16_LE", "-c", "1", "-r", "16000", "-t", "raw" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}

/// <summary>
/// Records on macOS with SoX's <c>rec</c>.
/// </summary>
public sealed class MacOSVoiceRecognitionService : ProcessVoiceRecognitionService
{
    /// <inheritdoc />
    protected override ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo("rec");
        foreach (var argument in new[] { "-q", "-c", "1", "-r", "16000", "-b", "16", "-e", "signed-integer", "-t", "raw", "-" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
