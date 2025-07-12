using SmartVoiceAgent.Infrastructure.Services.Voice;
using System.Diagnostics;

public class MacOSVoiceRecognitionService : VoiceRecognitionServiceBase
{
    private Process _recordProcess;
    private Task _readTask;
    private CancellationTokenSource _cancellationTokenSource;

    protected override void StartListeningInternal()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        _recordProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "-c \"rec -c 1 -r 16000 -b 16 -e signed-integer -t raw -\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        _recordProcess.Start();

        _readTask = Task.Run(async () =>
        {
            try
            {
                var buffer = new byte[4096];
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    int bytesRead = await _recordProcess.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token);
                    if (bytesRead > 0)
                    {
                        var actualData = new byte[bytesRead];
                        Array.Copy(buffer, actualData, bytesRead);
                        AddAudioData(actualData);
                    }
                    else
                    {
                        await Task.Delay(50);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal durma
            }
            catch (Exception ex)
            {
                InvokeOnError(ex);
            }
        }, _cancellationTokenSource.Token);
    }

    protected override void StopListeningInternal()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_recordProcess != null && !_recordProcess.HasExited)
            {
                _recordProcess.Kill();
                _recordProcess.WaitForExit(1000);
            }

            _readTask?.Wait(1000);

            var audioData = GetAndClearAudioData();
            OnListeningComplete(audioData);
        }
        catch (Exception ex)
        {
            InvokeOnError(ex);
        }
    }

    protected override void CleanupPlatformResources()
    {
        _cancellationTokenSource?.Dispose();
        _recordProcess?.Dispose();
        _recordProcess = null;
        _cancellationTokenSource = null;
        _readTask = null;
    }
}
