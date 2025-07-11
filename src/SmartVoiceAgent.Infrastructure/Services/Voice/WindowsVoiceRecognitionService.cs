using NAudio.Wave;
using SmartVoiceAgent.Infrastructure.Services.Voice;


public class WindowsVoiceRecognitionService : VoiceRecognitionServiceBase
{
    private WaveInEvent _waveIn;

    protected override void StartRecordingInternal()
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1), // 16-bit, mono, 16kHz
            BufferMilliseconds = 100
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStoppedHandler;
        _waveIn.StartRecording();
    }

    protected override void StopRecordingInternal()
    {
        _waveIn?.StopRecording();
    }

    protected override void CleanupPlatformResources()
    {
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStoppedHandler;
            _waveIn.Dispose();
            _waveIn = null;
        }
    }

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        try
        {
            lock (_lock)
            {
                if (_memoryStream != null && e.BytesRecorded > 0)
                {
                    _memoryStream.Write(e.Buffer, 0, e.BytesRecorded);
                }
            }
        }
        catch (Exception ex)
        {
            InvokeOnError(ex);
        }
    }

    private void OnRecordingStoppedHandler(object sender, StoppedEventArgs e)
    {
        lock (_lock)
        {
            try
            {
                if (e.Exception != null)
                {
                    InvokeOnError(e.Exception);
                    return;
                }

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
    }
}