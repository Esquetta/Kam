using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;

public class LinuxVoiceRecognitionService : IVoiceRecognitionService, IDisposable
{
    private Process _recordProcess;
    private MemoryStream _memoryStream;
    private bool _isRecording;
    private bool _disposed;

    public event EventHandler<byte[]> OnVoiceCaptured;
    public event EventHandler<Exception> OnError;
    public event EventHandler OnRecordingStarted;
    public event EventHandler OnRecordingStopped;

    public void StartRecording()
    {
        if (_isRecording)
            throw new InvalidOperationException("Kayıt zaten devam ediyor.");

        _memoryStream = new MemoryStream();
        _recordProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "arecord",
                Arguments = "-f S16_LE -c 1 -r 16000 -t raw",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        _recordProcess.Start();
        _recordProcess.StandardOutput.BaseStream.CopyToAsync(_memoryStream);

        _isRecording = true;
        OnRecordingStarted?.Invoke(this, EventArgs.Empty);
    }

    public void StopRecording()
    {
        if (!_isRecording)
            return;

        _recordProcess?.Kill();
        _recordProcess?.WaitForExit();
        _recordProcess?.Dispose();

        var audioData = _memoryStream.ToArray();
        OnVoiceCaptured?.Invoke(this, audioData);

        OnRecordingStopped?.Invoke(this, EventArgs.Empty);
        CleanupResources();
    }

    private void CleanupResources()
    {
        _memoryStream?.Dispose();
        _recordProcess = null;
        _memoryStream = null;
        _isRecording = false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_isRecording)
                StopRecording();

            CleanupResources();
            _disposed = true;
        }
    }
}
