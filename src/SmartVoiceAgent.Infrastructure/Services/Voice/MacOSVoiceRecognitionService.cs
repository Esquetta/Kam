using SmartVoiceAgent.Infrastructure.Services.Voice;
using System.Diagnostics;

public class MacOSVoiceRecognitionService : VoiceRecognitionServiceBase
{
    private Process _recordProcess;
    private Task _readTask;
    private CancellationTokenSource _cancellationTokenSource;

    protected override void StartRecordingInternal()
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

        // Async olarak output'u oku
        _readTask = Task.Run(async () =>
        {
            try
            {
                await _recordProcess.StandardOutput.BaseStream.CopyToAsync(_memoryStream, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Normal durma işlemi
            }
            catch (Exception ex)
            {
                InvokeOnError(ex);
            }
        });
    }

    protected override void StopRecordingInternal()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_recordProcess != null && !_recordProcess.HasExited)
            {
                _recordProcess.Kill();
                _recordProcess.WaitForExit(1000); // 1 saniye bekle
            }

            _readTask?.Wait(1000); // Read task'ının bitmesini bekle

            byte[] audioData = null;
            if (_memoryStream != null && _memoryStream.Length > 0)
            {
                audioData = _memoryStream.ToArray();
            }

            OnRecordingComplete(audioData);
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