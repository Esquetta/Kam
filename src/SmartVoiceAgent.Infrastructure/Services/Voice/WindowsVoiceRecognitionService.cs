using NAudio.Wave;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

public class WindowsVoiceRecognitionService : VoiceRecognitionServiceBase
{
    private WaveInEvent _waveIn;

    protected override void StartListeningInternal()
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 100
        };

        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStoppedHandler;
        _waveIn.StartRecording();
    }

    protected override void StopListeningInternal()
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
            if (e.BytesRecorded > 0)
            {
                // Safety check: ensure we don't read past the buffer length
                int bytesToCopy = Math.Min(e.BytesRecorded, e.Buffer.Length);
                if (bytesToCopy > 0)
                {
                    var buffer = new byte[bytesToCopy];
                    Buffer.BlockCopy(e.Buffer, 0, buffer, 0, bytesToCopy);
                    AddAudioData(buffer);
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
        try
        {
            if (e.Exception != null)
            {
                InvokeOnError(e.Exception);
                return;
            }

            StopListeningInternal();
        }
        catch (Exception ex)
        {
            InvokeOnError(ex);
        }
    }
}
